package dev.hatch.sync

import kotlinx.serialization.json.Json
import java.io.File
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlin.test.fail

// Reads the C# golden fixture in place, by relative path — never a copy. Per ADR-0003 the
// shared fixture is the anti-drift mechanism: copying it into mobile/ would reintroduce
// exactly the drift the monorepo exists to prevent.
//
// File I/O is platform-specific, so this lives in jvmTest rather than commonTest.
class GoldenFixtureTest {

    private val fixtureRelativePath = "windows/Hatch.Tests.Unit/Fixtures/tasks-golden.json"

    private fun fixture(): File {
        var dir: File? = File(System.getProperty("user.dir")).absoluteFile
        while (dir != null) {
            val candidate = File(dir, fixtureRelativePath)
            if (candidate.exists()) return candidate
            dir = dir.parentFile
        }
        fail("could not locate $fixtureRelativePath from ${System.getProperty("user.dir")}")
    }

    @Test
    fun reads_the_csharp_golden_fixture_in_place() {
        val file = fixture()
        assertTrue(file.exists(), "fixture not found at ${file.absolutePath}")
        assertTrue(
            file.absolutePath.replace('\\', '/').contains("Hatch.Tests.Unit/Fixtures"),
            "fixture must be read from the C# test project, not a copy: ${file.absolutePath}",
        )
    }

    @Test
    fun restores_every_field_from_the_golden_fixture() {
        val data = SyncWire.deserialize(fixture().readText())

        assertEquals(2, data.tasks.size)
        assertEquals(1, data.lists.size)

        val plants = data.tasks[0]
        assertEquals("11111111-1111-1111-1111-111111111111", plants.id)
        assertEquals("Water the plants", plants.title)
        assertEquals(false, plants.isCompleted)
        assertEquals(null, plants.completedAt)
        assertEquals(true, plants.isStarred)
        assertEquals(true, plants.isInMyDay)
        assertEquals("2026-01-15", plants.myDayDate)
        assertEquals("2026-01-16T09:00:00+00:00", plants.dueDate)
        assertEquals("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", plants.listId)
        assertEquals(3, plants.recurrence)
        assertEquals(3, plants.priority)
        assertEquals(listOf("home", "green"), plants.tags)
        assertEquals("2026-01-02T03:04:05Z", plants.createdAt)
        assertEquals("2026-01-15T10:20:30+00:00", plants.updatedAt)
        assertEquals("Kitchen and balcony", plants.notes)

        val ship = data.tasks[1]
        assertEquals("Ship v1", ship.title)
        assertEquals(true, ship.isCompleted)
        assertEquals("2026-01-10T08:00:00+00:00", ship.completedAt)
        assertEquals("00000000-0000-0000-0000-000000000000", ship.listId)
        assertEquals(emptyList(), ship.tags)
        assertEquals(null, ship.notes)
        assertEquals(null, ship.dueDate)
        assertEquals(null, ship.myDayDate)

        val home = data.lists[0]
        assertEquals("Home", home.name)
        assertEquals("#0078D4", home.accentColor)
        assertEquals(true, home.isPinned)
        assertEquals(0, home.sortOrder)
        assertEquals("🌿", home.customIcon)
        assertEquals("2026-01-05T12:00:00+00:00", home.updatedAt)
    }

    @Test
    fun re_serializes_value_equal_to_the_golden_fixture() {
        val original = fixture().readText()
        val reserialized = SyncWire.serialize(SyncWire.deserialize(original))

        // Value-equality, not text-equality: the fixture is pretty-printed and the writer is
        // not. §4 pins the shape, not the whitespace.
        assertEquals(
            Json.parseToJsonElement(original),
            Json.parseToJsonElement(reserialized),
        )
    }

    @Test
    fun ignores_unknown_properties() {
        val withNoise = """
            {"Tasks":[{"Id":"11111111-1111-1111-1111-111111111111","Title":"x",
            "IsCompleted":false,"CompletedAt":null,"IsStarred":false,"IsInMyDay":false,
            "MyDayDate":null,"DueDate":null,"ListId":"00000000-0000-0000-0000-000000000000",
            "Recurrence":0,"Priority":0,"Tags":[],"CreatedAt":"2026-01-01T00:00:00Z",
            "UpdatedAt":"2026-01-01T00:00:00Z","Notes":null,
            "HasRecurrence":true,"ShowAddDateHint":false}],"Lists":[]}
        """.trimIndent()

        val data = SyncWire.deserialize(withNoise)
        assertEquals("x", data.tasks.single().title)
    }
}
