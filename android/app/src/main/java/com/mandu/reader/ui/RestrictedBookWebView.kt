package com.mandu.reader.ui

import android.annotation.SuppressLint
import android.graphics.Color
import android.net.Uri
import android.webkit.WebResourceRequest
import android.webkit.WebResourceResponse
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.viewinterop.AndroidView
import com.mandu.reader.core.OpenBook
import com.mandu.reader.core.ReadingUnit
import java.io.ByteArrayInputStream
import java.util.Locale

private const val BOOK_SCHEME = "https"
private const val BOOK_HOST = "appassets.androidplatform.net"

@SuppressLint("SetJavaScriptEnabled")
@Composable
fun RestrictedBookWebView(openBook: OpenBook, unit: ReadingUnit, targetFragment: String? = null, modifier: Modifier = Modifier) {
    val context = LocalContext.current
    val webView = remember(openBook) {
        WebView(context).apply {
            // A paper-white default keeps unstyled EPUB text legible; publication CSS
            // remains free to paint its own page background.
            setBackgroundColor(Color.WHITE)
            settings.apply {
                javaScriptEnabled = false
                javaScriptCanOpenWindowsAutomatically = false
                domStorageEnabled = false
                databaseEnabled = false
                allowFileAccess = false
                allowContentAccess = false
                blockNetworkLoads = true
                mixedContentMode = WebSettings.MIXED_CONTENT_NEVER_ALLOW
                cacheMode = WebSettings.LOAD_NO_CACHE
                loadsImagesAutomatically = true
                builtInZoomControls = true
                displayZoomControls = false
                setSupportMultipleWindows(false)
                safeBrowsingEnabled = true
            }
            webViewClient = BookOnlyClient(openBook)
        }
    }
    DisposableEffect(webView) {
        onDispose {
            webView.stopLoading()
            webView.loadUrl("about:blank")
            webView.clearHistory()
            webView.removeAllViews()
            webView.destroy()
        }
    }
    val path = unit.locator.resource.substringBefore('#').trimStart('/')
    val fragment = targetFragment ?: unit.locator.resource.substringAfter('#', "")
    val target = Uri.Builder().scheme(BOOK_SCHEME).authority(BOOK_HOST)
        .appendEncodedPath(path.split('/').joinToString("/") { Uri.encode(it) })
        .fragment(fragment.ifBlank { null }).build().toString()
    AndroidView(
        factory = { webView },
        modifier = modifier,
        update = { view ->
            if (view.tag != target) {
                view.tag = target
                view.loadUrl(target)
            }
        }
    )
}

private class BookOnlyClient(private val openBook: OpenBook) : WebViewClient() {
    override fun shouldInterceptRequest(view: WebView?, request: WebResourceRequest): WebResourceResponse {
        val uri = request.url
        if (uri.scheme != BOOK_SCHEME || uri.host != BOOK_HOST) return denied()
        val path = Uri.decode(uri.encodedPath.orEmpty().trimStart('/'))
        if (path.isBlank() || path.split('/').any { it == ".." }) return denied()
        val bytes = runCatching { openBook.resource(path) }.getOrNull() ?: return missing(path)
        val type = mimeType(path, bytes)
        return WebResourceResponse(
            type,
            if (type.startsWith("text/") || type.contains("xml") || type.contains("javascript")) "UTF-8" else null,
            200,
            "OK",
            mapOf(
                "Content-Security-Policy" to "default-src 'none'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; font-src 'self' data:; script-src 'none'; connect-src 'none'; frame-src 'none'; object-src 'none'; media-src 'self'",
                "Cache-Control" to "no-store",
                "X-Content-Type-Options" to "nosniff"
            ),
            ByteArrayInputStream(bytes)
        )
    }

    override fun shouldOverrideUrlLoading(view: WebView?, request: WebResourceRequest): Boolean {
        val uri = request.url
        return uri.scheme != BOOK_SCHEME || uri.host != BOOK_HOST
    }

    @Suppress("DEPRECATION")
    override fun shouldOverrideUrlLoading(view: WebView?, url: String?): Boolean {
        val uri = runCatching { Uri.parse(url) }.getOrNull() ?: return true
        return uri.scheme != BOOK_SCHEME || uri.host != BOOK_HOST
    }

    private fun denied() = WebResourceResponse(
        "text/plain", "UTF-8", 403, "Blocked", emptyMap(), ByteArrayInputStream("External resources are blocked".toByteArray())
    )

    private fun missing(path: String) = WebResourceResponse(
        "text/plain", "UTF-8", 404, "Not Found", emptyMap(), ByteArrayInputStream("Missing book resource: $path".toByteArray())
    )
}

private fun mimeType(path: String, bytes: ByteArray): String = when (path.substringAfterLast('.', "").lowercase(Locale.ROOT)) {
    "xhtml", "xht" -> {
        // Many EPUB 2 books use an XHTML suffix for legacy non-namespaced HTML.
        // Chromium renders those as an XML source tree if strict XHTML is forced.
        // Correctly namespaced XHTML remains on the strict XML path.
        val prologue = bytes.decodeToString(0, minOf(bytes.size, 8192)).lowercase(Locale.ROOT)
        if (Regex("<html\\b[^>]*\\bxmlns\\s*=\\s*['\"]http://www\\.w3\\.org/1999/xhtml['\"]")
                .containsMatchIn(prologue)) "application/xhtml+xml" else "text/html"
    }
    "html", "htm" -> "text/html"
    "css" -> "text/css"
    "svg" -> "image/svg+xml"
    "jpg", "jpeg" -> "image/jpeg"
    "png" -> "image/png"
    "gif" -> "image/gif"
    "webp" -> "image/webp"
    "bmp" -> "image/bmp"
    "ttf" -> "font/ttf"
    "otf" -> "font/otf"
    "woff" -> "font/woff"
    "woff2" -> "font/woff2"
    "xml", "opf", "ncx" -> "application/xml"
    else -> "application/octet-stream"
}
