package com.mandu.reader;

import android.content.Intent;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.graphics.Bitmap;
import android.graphics.Color;
import android.os.Bundle;
import android.os.CancellationSignal;
import android.os.ParcelFileDescriptor;
import android.provider.DocumentsContract;
import android.provider.DocumentsContract.Document;
import android.provider.DocumentsContract.Root;
import android.provider.DocumentsProvider;
import java.io.*;
import java.util.zip.ZipEntry;
import java.util.zip.ZipOutputStream;

/** Real DocumentsProvider exposing generated files only. This class is never in the reader APK. */
public class ImportTestDocumentsProvider extends DocumentsProvider {
    private File root() {
        File root = new File(getContext().getCacheDir(), "import-provider-fixtures");
        root.mkdirs();
        return root;
    }
    private File file(String id) {
        File result = "root".equals(id) ? root() : new File(root(), id);
        try {
            if (!result.getCanonicalPath().equals(root().getCanonicalPath()) &&
                !result.getCanonicalPath().startsWith(root().getCanonicalPath() + "/"))
                throw new IllegalArgumentException("Invalid fixture path");
        } catch (IOException error) { throw new IllegalArgumentException(error); }
        return result;
    }
    @Override public boolean onCreate() { return true; }
    @Override public Cursor queryRoots(String[] projection) {
        MatrixCursor result = new MatrixCursor(new String[] {Root.COLUMN_ROOT_ID, Root.COLUMN_TITLE,
            Root.COLUMN_DOCUMENT_ID, Root.COLUMN_FLAGS, Root.COLUMN_MIME_TYPES});
        result.addRow(new Object[] {"fixture", "Import regression fixtures", "root",
            Root.FLAG_SUPPORTS_IS_CHILD | Root.FLAG_LOCAL_ONLY, "*/*"});
        return result;
    }
    private MatrixCursor cursor(String[] projection) {
        return new MatrixCursor(projection != null ? projection : new String[] {Document.COLUMN_DOCUMENT_ID,
            Document.COLUMN_DISPLAY_NAME, Document.COLUMN_MIME_TYPE, Document.COLUMN_SIZE,
            Document.COLUMN_LAST_MODIFIED, Document.COLUMN_FLAGS});
    }
    private void addFile(MatrixCursor cursor, String id) {
        File file = file(id);
        MatrixCursor.RowBuilder row = cursor.newRow();
        for (String column : cursor.getColumnNames()) {
            Object value = null;
            switch (column) {
                case Document.COLUMN_DOCUMENT_ID: value = id; break;
                case Document.COLUMN_DISPLAY_NAME: value = file.getName(); break;
                case Document.COLUMN_MIME_TYPE: value = file.isDirectory() ? Document.MIME_TYPE_DIR :
                    file.getName().endsWith(".png") ? "image/png" : "application/vnd.comicbook+zip"; break;
                case Document.COLUMN_SIZE: value = file.length(); break;
                case Document.COLUMN_LAST_MODIFIED: value = file.lastModified(); break;
                case Document.COLUMN_FLAGS: value = 0; break;
            }
            row.add(column, value);
        }
    }
    @Override public Cursor queryDocument(String id, String[] projection) throws FileNotFoundException {
        if (!file(id).exists()) throw new FileNotFoundException(id);
        MatrixCursor result = cursor(projection); addFile(result, id); return result;
    }
    @Override public Cursor queryChildDocuments(String parent, String[] projection, String sortOrder) {
        MatrixCursor result = cursor(projection);
        // Keep root identity stable: DocumentsUI remembers directory stacks between launches.
        // Show the current generated session only, then select it through the real picker.
        String selected = getContext().getSharedPreferences("picker-root", 0).getString("id", null);
        if ("root".equals(parent) && selected != null) {
            addFile(result, selected);
            return result;
        }
        File[] children = file(parent).listFiles();
        if (children != null) for (File child : children)
            addFile(result, child.getAbsolutePath().substring(root().getAbsolutePath().length() + 1));
        return result;
    }
    @Override public ParcelFileDescriptor openDocument(String id, String mode, CancellationSignal signal) throws FileNotFoundException {
        if (!"r".equals(mode)) throw new FileNotFoundException("Read-only fixtures");
        if (id.startsWith("slow-")) android.os.SystemClock.sleep(250);
        return ParcelFileDescriptor.open(file(id), ParcelFileDescriptor.MODE_READ_ONLY);
    }
    @Override public boolean isChildDocument(String parent, String child) {
        return file(child).getAbsolutePath().startsWith(file(parent).getAbsolutePath() + "/");
    }
    @Override public Bundle call(String method, String arg, Bundle extras) {
        if ("make-fixtures".equals(method) || "make-picker-fixtures".equals(method)) {
            File directory = file(arg); directory.mkdirs();
            Bitmap bitmap = Bitmap.createBitmap(48, 72, Bitmap.Config.ARGB_8888);
            bitmap.eraseColor(Color.rgb(220, 55, 50));
            ByteArrayOutputStream png = new ByteArrayOutputStream();
            bitmap.compress(Bitmap.CompressFormat.PNG, 100, png); bitmap.recycle();
            byte[] bytes = png.toByteArray();
            try {
                for (int i = 1; i <= 12; i++) {
                    try (ZipOutputStream zip = new ZipOutputStream(new FileOutputStream(new File(directory, "卷" + i + ".cbz")))) {
                        for (String name : new String[] {"10.png", "2.png", "1.png"}) {
                            zip.putNextEntry(new ZipEntry(name)); zip.write(bytes); zip.closeEntry();
                        }
                    }
                }
                File images = new File(directory, "图片册"); images.mkdirs();
                for (String name : new String[] {"10.png", "2.png", "1.png"})
                    try (OutputStream out = new FileOutputStream(new File(images, name))) { out.write(bytes); }
                try (OutputStream out = new FileOutputStream(new File(directory, "损坏.cbz"))) { out.write(new byte[] {1, 2, 3}); }
            } catch (IOException error) { throw new IllegalStateException(error); }
            if ("make-fixtures".equals(method)) {
                getContext().grantUriPermission("com.mandu.reader", DocumentsContract.buildTreeDocumentUri("com.mandu.reader.test.documents", arg),
                    Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_PREFIX_URI_PERMISSION | Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION);
            } else {
                getContext().getSharedPreferences("picker-root", 0).edit().putString("id", arg).commit();
                getContext().getContentResolver().notifyChange(DocumentsContract.buildRootsUri("com.mandu.reader.test.documents"), null);
            }
            return new Bundle();
        }
        if ("remove-fixtures".equals(method)) {
            if (arg == null || !arg.startsWith("slow-")) throw new IllegalArgumentException("Not a generated fixture");
            remove(file(arg));
            if (arg.equals(getContext().getSharedPreferences("picker-root", 0).getString("id", "root"))) {
                getContext().getSharedPreferences("picker-root", 0).edit().remove("id").commit();
                getContext().getContentResolver().notifyChange(DocumentsContract.buildRootsUri("com.mandu.reader.test.documents"), null);
            }
            return new Bundle();
        }
        return super.call(method, arg, extras);
    }
    private void remove(File file) {
        File[] children = file.listFiles();
        if (children != null) for (File child : children) remove(child);
        file.delete();
    }
}
