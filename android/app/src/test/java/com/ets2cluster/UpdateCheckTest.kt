package com.ets2cluster

import org.junit.Assert.*
import org.junit.Test

class UpdateCheckTest {
    @Test fun versions() {
        assertEquals(listOf(1, 3, 0), UpdateCheck.version("v1.3.0"))
        assertEquals(listOf(1, 3, 0), UpdateCheck.version("1.3"))
        assertNull(UpdateCheck.version("latest"))
        assertTrue(UpdateCheck.newer(listOf(1, 10, 0), listOf(1, 9, 9)))
        assertFalse(UpdateCheck.newer(listOf(1, 3, 0), listOf(1, 3, 0)))
    }

    private fun rel(tag: String, apk: Boolean, pre: Boolean = false, url: String = "https://github.com/x/y/releases/tag/$tag") =
        """{"tag_name":"$tag","draft":false,"prerelease":$pre,"html_url":"$url","assets":[{"name":"${if (apk) "app.apk" else "setup.exe"}"}]}"""

    @Test fun picksNewestReleaseWithApk() {
        val json = "[" + rel("v1.4.0", false) + "," + rel("v1.3.1", true) + "," + rel("v1.3.0", true) + "]"
        assertEquals("1.3.1", UpdateCheck.fromReleases(json, "1.3.0")?.latest)
        assertNull(UpdateCheck.fromReleases(json, "1.3.1"))
    }

    @Test fun ignoresPrereleasesAndForeignLinks() {
        assertNull(UpdateCheck.fromReleases("[" + rel("v2.0.0", true, pre = true) + "]", "1.0.0"))
        val u = UpdateCheck.fromReleases("[" + rel("v2.0.0", true, url = "https://evil.example/x") + "]", "1.0.0")
        assertEquals(UpdateCheck.RELEASES_PAGE, u?.pageUrl)
    }
}
