package dev.hatch.sync

import kotlinx.datetime.DateTimeUnit
import kotlinx.datetime.DayOfWeek
import kotlinx.datetime.LocalDate
import kotlinx.datetime.plus

// Wire values for TodoItem.Recurrence (§4).
object Recurrence {
    const val NONE = 0
    const val DAILY = 1
    const val WEEKDAYS = 2
    const val WEEKLY = 3
    const val MONTHLY = 4
}

// Kotlin transcription of windows/Helpers/RecurrenceHelper.cs.
object RecurrenceHelper {

    // Anchored to the original due date so a Monday task stays on Monday when completed late.
    // The date is read from the ISO text as written — converting through a time zone would
    // shift it back a day west of UTC (see RecurrenceHelperTest).
    fun advanceDueDate(dueIso: String, recurrence: Int): String? {
        val date = calendarDateOf(dueIso) ?: return null
        val next = when (recurrence) {
            Recurrence.DAILY -> date.plus(1, DateTimeUnit.DAY)
            Recurrence.WEEKLY -> date.plus(7, DateTimeUnit.DAY)
            // Clamps into a shorter month (Jan 31 -> Feb 28), as AddMonths does.
            Recurrence.MONTHLY -> date.plus(1, DateTimeUnit.MONTH)
            Recurrence.WEEKDAYS -> advanceSkippingWeekend(date)
            else -> date
        }
        return wireForm(next)
    }

    fun calendarDateOf(iso: String): LocalDate? =
        runCatching { LocalDate.parse(iso.take(10)) }.getOrNull()

    fun wireForm(date: LocalDate): String = "${date}T00:00:00+00:00"

    private fun advanceSkippingWeekend(date: LocalDate): LocalDate {
        var next = date.plus(1, DateTimeUnit.DAY)
        while (next.dayOfWeek == DayOfWeek.SATURDAY || next.dayOfWeek == DayOfWeek.SUNDAY) {
            next = next.plus(1, DateTimeUnit.DAY)
        }
        return next
    }
}
