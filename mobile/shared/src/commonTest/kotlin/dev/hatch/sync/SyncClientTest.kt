package dev.hatch.sync

import kotlin.test.Test
import kotlin.test.assertEquals

class SyncClientTest {

    // windows/Services/Secrets.cs stores the URL with the REST path appended; supabase-kt
    // adds /rest/v1 itself, so passing it through unchanged yields /rest/v1/rest/v1/.
    @Test
    fun strips_the_rest_path_from_the_windows_secrets_url() {
        assertEquals(
            "https://cwgasedfewjarujvsesy.supabase.co",
            SyncClient.normalizeUrl("https://cwgasedfewjarujvsesy.supabase.co/rest/v1/"),
        )
    }

    @Test
    fun accepts_a_bare_project_url_unchanged() {
        assertEquals(
            "https://cwgasedfewjarujvsesy.supabase.co",
            SyncClient.normalizeUrl("https://cwgasedfewjarujvsesy.supabase.co"),
        )
    }

    @Test
    fun tolerates_trailing_slash_and_whitespace() {
        assertEquals(
            "https://example.supabase.co",
            SyncClient.normalizeUrl("  https://example.supabase.co/  "),
        )
    }
}
