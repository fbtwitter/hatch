package dev.hatch.sync

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

// Mirrors windows/Hatch.Tests.Unit/RecurrenceHelperTests.cs case for case.
//
// Both sides read the date as written rather than through a time-zone conversion, which
// would shift it back a day west of UTC. The C# helper originally did ToLocalTime().Date
// first; fixed 2026-07-28 to match this port.
class RecurrenceHelperTest {

    private fun utc(y: Int, m: Int, d: Int) =
        "$y-${m.toString().padStart(2, '0')}-${d.toString().padStart(2, '0')}T00:00:00+00:00"

    @Test
    fun daily_advances_by_one_day() {
        assertEquals(utc(2026, 7, 11), RecurrenceHelper.advanceDueDate(utc(2026, 7, 10), Recurrence.DAILY))
    }

    @Test
    fun weekly_advances_by_seven_days() {
        assertEquals(utc(2026, 7, 17), RecurrenceHelper.advanceDueDate(utc(2026, 7, 10), Recurrence.WEEKLY))
    }

    @Test
    fun monthly_advances_by_one_calendar_month() {
        assertEquals(utc(2026, 8, 31), RecurrenceHelper.advanceDueDate(utc(2026, 7, 31), Recurrence.MONTHLY))
    }

    @Test
    fun monthly_clamps_into_a_shorter_month() {
        assertEquals(utc(2026, 2, 28), RecurrenceHelper.advanceDueDate(utc(2026, 1, 31), Recurrence.MONTHLY))
    }

    @Test
    fun weekdays_friday_advances_to_monday() {
        // 2026-07-10 is a Friday.
        assertEquals(utc(2026, 7, 13), RecurrenceHelper.advanceDueDate(utc(2026, 7, 10), Recurrence.WEEKDAYS))
    }

    @Test
    fun weekdays_monday_advances_to_tuesday() {
        // 2026-07-13 is a Monday.
        assertEquals(utc(2026, 7, 14), RecurrenceHelper.advanceDueDate(utc(2026, 7, 13), Recurrence.WEEKDAYS))
    }

    @Test
    fun none_returns_the_same_calendar_date() {
        assertEquals(utc(2026, 7, 10), RecurrenceHelper.advanceDueDate(utc(2026, 7, 10), Recurrence.NONE))
    }

    @Test
    fun result_is_always_zero_offset() {
        val next = RecurrenceHelper.advanceDueDate("2026-07-10T23:00:00-05:00", Recurrence.DAILY)
        assertEquals(utc(2026, 7, 11), next)
    }

    @Test
    fun a_legacy_due_date_with_a_time_component_still_advances_by_calendar_date() {
        assertEquals(utc(2026, 1, 17), RecurrenceHelper.advanceDueDate("2026-01-16T09:00:00+00:00", Recurrence.DAILY))
    }

    @Test
    fun unparseable_due_date_returns_null_rather_than_guessing() {
        assertNull(RecurrenceHelper.advanceDueDate("not a date", Recurrence.DAILY))
    }
}
