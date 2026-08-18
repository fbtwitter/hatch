package dev.hatch.sync

import kotlinx.datetime.LocalDate
import kotlin.test.Test
import kotlin.test.assertEquals

// Mirrors windows/Helpers/DueDatePresets.cs. 2026-07-13 is a Monday, so the week used
// throughout runs Mon 13th → Sun 19th, with Saturday the 18th.
class DueDatePresetsTest {

    private fun date(y: Int, m: Int, d: Int) = LocalDate(y, m, d)

    @Test
    fun today_is_the_day_itself() {
        assertEquals(date(2026, 7, 15), DueDatePresets.today(date(2026, 7, 15)))
    }

    @Test
    fun tomorrow_advances_by_one_day() {
        assertEquals(date(2026, 7, 16), DueDatePresets.tomorrow(date(2026, 7, 15)))
    }

    @Test
    fun tomorrow_crosses_a_month_boundary() {
        assertEquals(date(2026, 8, 1), DueDatePresets.tomorrow(date(2026, 7, 31)))
    }

    @Test
    fun this_weekend_from_midweek_is_the_coming_saturday() {
        // Wednesday the 15th.
        assertEquals(date(2026, 7, 18), DueDatePresets.thisWeekend(date(2026, 7, 15)))
    }

    @Test
    fun this_weekend_on_saturday_is_today() {
        assertEquals(date(2026, 7, 18), DueDatePresets.thisWeekend(date(2026, 7, 18)))
    }

    @Test
    fun this_weekend_on_sunday_snaps_to_next_saturday() {
        // The weekend has already passed, so offering yesterday would be useless.
        assertEquals(date(2026, 7, 25), DueDatePresets.thisWeekend(date(2026, 7, 19)))
    }

    @Test
    fun next_week_from_midweek_is_the_coming_monday() {
        assertEquals(date(2026, 7, 20), DueDatePresets.nextWeek(date(2026, 7, 15)))
    }

    @Test
    fun next_week_on_monday_is_a_full_week_out() {
        assertEquals(date(2026, 7, 20), DueDatePresets.nextWeek(date(2026, 7, 13)))
    }

    @Test
    fun next_week_on_sunday_is_the_very_next_day() {
        assertEquals(date(2026, 7, 20), DueDatePresets.nextWeek(date(2026, 7, 19)))
    }
}
