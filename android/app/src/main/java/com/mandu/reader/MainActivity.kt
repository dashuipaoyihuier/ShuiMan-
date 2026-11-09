package com.mandu.reader

import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.view.KeyEvent
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import com.mandu.reader.data.BookRepository
import com.mandu.reader.ui.ManduApp
import com.mandu.reader.ui.ManduTheme

class MainActivity : ComponentActivity() {
    // Owned by the currently visible reading session; sheets and library leave it null.
    var onVolumePage: ((Int) -> Unit)? = null
    private val heldVolumeKeys = mutableSetOf<Int>()

    override fun dispatchKeyEvent(event: KeyEvent): Boolean {
        val delta = when (event.keyCode) {
            KeyEvent.KEYCODE_VOLUME_DOWN -> 1
            KeyEvent.KEYCODE_VOLUME_UP -> -1
            else -> return super.dispatchKeyEvent(event)
        }
        if (event.action == KeyEvent.ACTION_UP && heldVolumeKeys.remove(event.keyCode)) return true
        if (event.action == KeyEvent.ACTION_DOWN) {
            if (event.keyCode in heldVolumeKeys) return true
            val callback = onVolumePage
            if (callback != null && event.repeatCount == 0) {
                heldVolumeKeys.add(event.keyCode)
                callback(delta)
                return true
            }
        }
        return super.dispatchKeyEvent(event)
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (!hasFocus) heldVolumeKeys.clear()
    }

    private lateinit var repository: BookRepository
    private var externalDocument by mutableStateOf<Uri?>(null)

    private val openFiles = registerForActivityResult(ActivityResultContracts.OpenMultipleDocuments()) { uris ->
        // The retained import worker persists grants on IO; a large selection must not
        // make hundreds of synchronous permission-service calls on the UI thread.
        pendingFiles = uris
    }

    private val openFolder = registerForActivityResult(ActivityResultContracts.OpenDocumentTree()) { uri ->
        uri ?: return@registerForActivityResult
        pendingFolder = uri
    }

    private var pendingFiles by mutableStateOf<List<Uri>>(emptyList())
    private var pendingFolder by mutableStateOf<Uri?>(null)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        repository = BookRepository(applicationContext)
        acceptViewIntent(intent)
        setContent {
            ManduTheme {
                ManduApp(
                    repository = repository,
                    externalDocument = externalDocument,
                    pendingFiles = pendingFiles,
                    pendingFolder = pendingFolder,
                    onFilesConsumed = { pendingFiles = emptyList() },
                    onFolderConsumed = { pendingFolder = null },
                    onExternalConsumed = { externalDocument = null },
                    onPickFiles = {
                        openFiles.launch(
                            // Providers often classify MOBI as generic binary data. Validate on import.
                            arrayOf("*/*")
                        )
                    },
                    onPickFolder = { openFolder.launch(null) }
                )
            }
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        acceptViewIntent(intent)
    }

    private fun acceptViewIntent(intent: Intent?) {
        if (intent?.action != Intent.ACTION_VIEW) return
        intent.data?.let { uri ->
            externalDocument = uri
        }
        // Prevent configuration recreation from importing the same ACTION_VIEW twice.
        intent.action = null
        intent.data = null
    }

}
