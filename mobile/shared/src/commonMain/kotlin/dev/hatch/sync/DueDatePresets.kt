package dev.hatch.sync

import kotlinx.datetime.DateTimeUnit
import kotlinx.datetime.LocalDate
import kotlinx.datetime.isoDayNumber
import kotlinx.datetime.plus

// Kotlin transcription of windows/Helpers/DueDatePresets.cs.
object DueDatePresets {

    fun today(today: LocalDate): LocalDate = today

    fun tomorrow(today: LocalDate): LocalDate = today.plus(1, DateTimeUnit.DAY)

    // Saturday of the current week. Sunday snaps to next Saturday (weekend already passed).
    fun thisWeekend(today: LocalDate): LocalDate =
        today.plus((SATURDAY - weekDayNumber(today) + 7) % 7, DateTimeUnit.DAY)

    // Monday of next week; if today is already Monday, return next Monday (+7).
    fun nextWeek(today: LocalDate): LocalDate {
        val daysUntilMonday = (MONDAY - weekDayNumber(today) + 7) % 7
        return today.plus(if (daysUntilMonday == 0) 7 else daysUntilMonday, DateTimeUnit.DAY)
    }

    // C# numbers the week from Sunday = 0; ISO numbers it from Monday = 1 with Sunday = 7.
    // Taking the ISO number modulo 7 lands exactly on the C# value, which is what lets the
    // two formulas above stay readable as transcriptions rather than re-derivations.
    private fun weekDayNumber(date: LocalDate) = date.dayOfWeek.isoDayNumber % 7

    private const val MONDAY = 1
    private const val SATURDAY = 6
}
