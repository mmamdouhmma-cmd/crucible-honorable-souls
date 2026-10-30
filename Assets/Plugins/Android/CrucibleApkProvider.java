package com.honorablesouls.crucible;

import android.content.ContentProvider;
import android.content.ContentValues;
import android.database.Cursor;
import android.net.Uri;
import android.os.ParcelFileDescriptor;

import java.io.File;
import java.io.FileNotFoundException;

/**
 * Lets Android's package installer read the update GameUpdater downloaded into the app's private cache.
 * It serves that one file, read-only, and only to whoever the install intent grants the URI to.
 * androidx FileProvider does the same job, but would add a dependency for thirty lines.
 */
public class CrucibleApkProvider extends ContentProvider {
    private static final String APK_MIME = "application/vnd.android.package-archive";
    private static final String APK_NAME = "update.apk";

    @Override public boolean onCreate() { return true; }

    @Override public ParcelFileDescriptor openFile(Uri uri, String mode) throws FileNotFoundException {
        if (!"r".equals(mode)) throw new FileNotFoundException("read only");
        return ParcelFileDescriptor.open(new File(getContext().getCacheDir(), APK_NAME), ParcelFileDescriptor.MODE_READ_ONLY);
    }

    @Override public String getType(Uri uri) { return APK_MIME; }

    @Override public Cursor query(Uri uri, String[] projection, String selection, String[] args, String order) { return null; }
    @Override public Uri insert(Uri uri, ContentValues values) { return null; }
    @Override public int delete(Uri uri, String selection, String[] args) { return 0; }
    @Override public int update(Uri uri, ContentValues values, String selection, String[] args) { return 0; }
}
