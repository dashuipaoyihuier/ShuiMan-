package com.mandu.reader;

import android.app.Activity;
import android.net.Uri;
import android.os.Bundle;

/** Java-only: the separate instrumentation APK process does not bundle the app's Kotlin runtime. */
public class ImportFixtureActivity extends Activity {
    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        getContentResolver().call(Uri.parse("content://com.mandu.reader.test.documents"),
            getIntent().getStringExtra("method"), getIntent().getStringExtra("fixture"), null);
        finish();
    }
}
