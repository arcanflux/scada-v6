# Plan: Replace Period Buttons with Custom Date Range Picker

## Understanding

You want to replace the current period buttons (1 day, 3 days, 7 days) in the combined chart toolbar with a custom date range picker that works as follows:

### Inactive State (Collapsed)
- A single compact **box/button** in the toolbar showing the currently selected date range
- Visual separation between **start date** and **end date** (e.g., `2026-03-20 14:00  —  2026-03-25 18:30`)
- Clicking this box opens the picker dropdown

### Active State (Expanded — opens downward)
The dropdown contains two sections (one for Start, one for End), each with:

1. **Time Scrollbar** (top, centered above calendar)
   - A horizontal scrollable strip representing 24 hours
   - Styled like a radio tuner dial: hour marks are **tall lines** with labels (00, 01, ... 23), minute marks are **shorter tick lines**
   - User drags/scrolls horizontally to set the time
   - Current selected time displayed clearly

2. **Calendar** (below the time scrollbar)
   - Shows a month grid with day, month, year navigation
   - User clicks a date to select it
   - Shows current month by default, with prev/next month arrows and a month/year header

### Workflow
- User clicks the box → dropdown opens with **Start** section first, then **End** section below
- User selects start date + time, then end date + time
- An **Apply** button at the bottom confirms the selection, closes the dropdown, and reloads the chart with the custom range
- The box updates to show the new date range

## File to Modify
- `/home/user/scada-v6/ScadaWeb/OpenPlugins/PlgMap/Areas/Map/Pages/MapView.cshtml` — all changes are in this single file (CSS + JS)

## Implementation Steps

### Step 1: Add CSS for the Date Range Picker
Add new CSS styles after existing `.map-chart-period-btn` styles:
- `.map-daterange-box` — the collapsed button showing current range
- `.map-daterange-dropdown` — the dropdown container (position: absolute, opens downward)
- `.map-daterange-section` — wrapper for each Start/End section
- `.map-daterange-section-label` — "Start" / "End" label
- `.map-time-scrollbar` — the horizontal time tuner strip
- `.map-time-scrollbar-track` — the scrollable inner track with tick marks
- `.map-time-tick`, `.map-time-tick-hour` — minute and hour tick marks
- `.map-time-indicator` — the center indicator line showing current selection
- `.map-time-display` — shows the currently selected time as text
- `.map-calendar` — the calendar grid container
- `.map-calendar-header` — month/year nav with prev/next arrows
- `.map-calendar-grid` — the 7-column day grid
- `.map-calendar-day`, `.map-calendar-day.selected`, `.map-calendar-day.today` — day cells
- `.map-daterange-apply` — the Apply button

### Step 2: Build the Date Range Picker in JavaScript
In `_openCombinedChart()`, replace the period button generation (lines 1293-1303) with:
1. Create the collapsed date range box showing start/end dates
2. Default to "now minus 1 day → now" (equivalent to current default)
3. Store `_chartStartTime` and `_chartEndTime` on the instance

### Step 3: Implement the Dropdown Component
Add new methods to `MapViewManager`:
- `_buildDateRangeDropdown()` — creates the full dropdown DOM
- `_buildTimeScrollbar(section)` — builds the horizontal tuner-style time selector with:
  - Canvas or DOM-based tick marks (24 major hour ticks, 60×24 minute ticks)
  - Drag-to-scroll interaction
  - Center indicator line
  - Returns selected hour:minute
- `_buildCalendar(section, initialDate)` — builds the month calendar grid:
  - Prev/next month arrows
  - Click-to-select day
  - Highlights today and selected date
- `_applyDateRange()` — reads selected start/end dates, closes dropdown, calls `_fetchHistDataRange()` and re-renders chart

### Step 4: Implement the Time Scrollbar (Tuner Dial)
- Render a wide inner track (e.g., 2880px for 24h at 2px per minute)
- Draw tick marks: tall lines at each hour (with hour label), small lines at each minute
- A fixed center indicator line
- User scrolls horizontally (mouse drag or scroll wheel) to pick time
- Snap to nearest minute on release
- Display selected time (HH:MM) above the scrollbar

### Step 5: Implement the Calendar
- Simple custom calendar (no external library — keeping consistency with the project)
- Month/year header with `<` `>` navigation
- 7-column grid (Mon-Sun), fill days of current month
- Click a day to select it
- Highlight selected day and today

### Step 6: Wire Up Apply & Chart Reload
- On Apply click: read both start and end datetime values
- Validate start < end
- Update the collapsed box display
- Call `_fetchHistDataRange(cnlNums, startTime, endTime)` (already exists)
- Re-render chart with `_renderCombinedChart()`
- Keep Reset Zoom and Status Toggle buttons working as before

### Step 7: Keep Existing Functionality
- Reset Zoom button stays (uses the custom date range instead of days)
- Status toggle button stays
- Pan-to-load continues to work
- Chart cache continues to work

## No New Files
Everything is self-contained in MapView.cshtml (inline CSS + JS), consistent with the existing architecture.
