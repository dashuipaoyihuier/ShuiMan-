package com.mandu.reader.data

import android.util.Log
import com.mandu.reader.core.ReadingState
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import java.util.concurrent.atomic.AtomicLong

/**
 * Process-wide state handoff. A newly-created writer for a book supersedes every
 * older reader session, and queued intermediate snapshots are skipped.
 */
class ReadingStateWriter(
    private val repository: BookRepository,
    private val bookId: String,
    private val onFailure: (Throwable) -> Unit = {}
) {
    private val session = Shared.begin(bookId)
    @Volatile private var lastJob: Job? = null
    @Volatile private var lastFailure: Throwable? = null

    /** Enqueues a durable snapshot without tying it to a screen's cancellable scope. */
    fun enqueue(state: ReadingState, completed: Boolean = false): Job {
        lastFailure = null
        return Shared.enqueue(
            repository = repository,
            bookId = bookId,
            session = session,
            state = state,
            completed = completed,
            onSuccess = { lastFailure = null },
            onFailure = { error ->
                lastFailure = error
                Shared.reportOnMain { onFailure(error) }
            }
        ).also { lastJob = it }
    }

    /** Waits until this writer's most recently enqueued snapshot has either saved or been superseded. */
    suspend fun flush() {
        lastJob?.join()
        lastFailure?.let { throw it }
    }

    private object Shared {
        private data class Request(val session: Long, val token: Long)

        private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
        private val writeMutex = Mutex()
        private val counter = AtomicLong()
        private val monitor = Any()
        private val activeSessions = mutableMapOf<String, Long>()
        private val latestRequests = mutableMapOf<String, Request>()

        fun begin(bookId: String): Long = synchronized(monitor) {
            counter.incrementAndGet().also { activeSessions[bookId] = it }
        }

        fun enqueue(
            repository: BookRepository,
            bookId: String,
            session: Long,
            state: ReadingState,
            completed: Boolean,
            onSuccess: () -> Unit,
            onFailure: (Throwable) -> Unit
        ): Job {
            val request = synchronized(monitor) {
                if (activeSessions[bookId] != session) return completedJob()
                Request(session, counter.incrementAndGet()).also { latestRequests[bookId] = it }
            }
            return scope.launch {
                writeMutex.withLock {
                    val current = synchronized(monitor) {
                        activeSessions[bookId] == session && latestRequests[bookId] == request
                    }
                    if (current) {
                        try {
                            repository.saveState(bookId, state, completed)
                            onSuccess()
                        } catch (cancelled: CancellationException) {
                            throw cancelled
                        } catch (error: Throwable) {
                            Log.e("ReadingStateWriter", "Failed to save state for $bookId", error)
                            onFailure(error)
                        }
                    }
                }
            }
        }

        private fun completedJob(): Job = SupervisorJob().apply { complete() }

        fun reportOnMain(block: () -> Unit) {
            scope.launch(Dispatchers.Main.immediate) {
                try { block() } catch (error: Throwable) {
                    Log.e("ReadingStateWriter", "State failure callback failed", error)
                }
            }
        }
    }
}
