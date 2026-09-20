const state = {
  events: [],
  sourceResults: [],
  selectedId: null,
  sourceStatusTab: "with-events"
};

const elements = {
  themeToggle: document.querySelector("#themeToggle"),
  filters: document.querySelector("#filters"),
  fromDate: document.querySelector("#fromDate"),
  toDate: document.querySelector("#toDate"),
  searchText: document.querySelector("#searchText"),
  sourceFilter: document.querySelector("#sourceFilter"),
  venueFilterLabel: document.querySelector("#venueFilterLabel"),
  venueFilter: document.querySelector("#venueFilter"),
  sortOrder: document.querySelector("#sortOrder"),
  eventCount: document.querySelector("#eventCount"),
  venueCount: document.querySelector("#venueCount"),
  sourceCount: document.querySelector("#sourceCount"),
  visibleCount: document.querySelector("#visibleCount"),
  windowLabel: document.querySelector("#windowLabel"),
  sourceTabs: document.querySelectorAll("[data-source-tab]"),
  sourcesWithEventsCount: document.querySelector("#sourcesWithEventsCount"),
  sourcesWithoutEventsCount: document.querySelector("#sourcesWithoutEventsCount"),
  sourceStatus: document.querySelector("#sourceStatus"),
  eventList: document.querySelector("#eventList"),
  eventDetail: document.querySelector("#eventDetail")
};

const sourceLabels = {
  "3olympia": "3Olympia",
  "abbey-theatre": "Abbey Theatre",
  "axis-ballymun": "Axis Ballymun",
  "button-factory": "Button Factory",
  "civic-theatre": "Civic Theatre",
  "dublin-ie": "Dublin.ie",
  "draiocht": "Draíocht",
  "entertainment-ie": "Entertainment.ie",
  "eventbrite": "Eventbrite",
  "evensos": "Evensos",
  "fringefest": "Dublin Fringe",
  "gaiety-theatre": "Gaiety Theatre",
  "gate-theatre": "Gate Theatre",
  "mcd": "MCD",
  "mill-theatre": "dlr Mill Theatre",
  "national-concert-hall": "National Concert Hall",
  "pavilion-theatre": "Pavilion Theatre",
  "rte-orchestra": "RTÉ Orchestra",
  "smock-alley": "Smock Alley",
  "ticketmaster": "Ticketmaster",
  "the-academy": "The Academy",
  "the-grand-social": "The Grand Social",
  "tickts": "Tickts",
  "vicar-street": "Vicar Street",
  "whats-on-dublin": "What's On Dublin",
  "whelans": "Whelan's",
  "workmans-club": "Workman's Club"
};

const sourceLogos = {
  "3olympia": "/assets/favicons/3olympia.png",
  "abbey-theatre": "/assets/favicons/abbey-theatre.png",
  "axis-ballymun": "/assets/favicons/axis-ballymun.png",
  "button-factory": "/assets/favicons/button-factory.png",
  "civic-theatre": "/assets/favicons/civic-theatre.png",
  "dublin-ie": "/assets/favicons/dublin-ie.png",
  "draiocht": "/assets/favicons/draiocht.png",
  "entertainment-ie": "/assets/favicons/entertainment-ie.png",
  "eventbrite": "/assets/favicons/eventbrite.png",
  "evensos": "/assets/favicons/evensos.png",
  "fringefest": "/assets/favicons/fringefest.png",
  "gaiety-theatre": "/assets/favicons/gaiety-theatre.png",
  "gate-theatre": "/assets/favicons/gate-theatre.png",
  "mcd": "/assets/favicons/mcd.png",
  "mill-theatre": "/assets/favicons/mill-theatre.png",
  "national-concert-hall": "/assets/favicons/national-concert-hall.png",
  "pavilion-theatre": "/assets/favicons/pavilion-theatre.png",
  "rte-orchestra": "/assets/favicons/rte-orchestra.png",
  "smock-alley": "/assets/favicons/smock-alley.png",
  "ticketmaster": "/assets/favicons/ticketmaster.png",
  "the-academy": "/assets/favicons/the-academy.png",
  "the-grand-social": "/assets/favicons/the-grand-social.png",
  "tickts": "/assets/favicons/tickts.png",
  "vicar-street": "/assets/favicons/vicar-street.png",
  "whats-on-dublin": "/assets/favicons/whats-on-dublin.png",
  "whelans": "/assets/favicons/whelans.png",
  "workmans-club": "/assets/favicons/workmans-club.png"
};

const sourceColors = {
  "3olympia": "#c62828",
  "abbey-theatre": "#6a3d9a",
  "axis-ballymun": "#d83a76",
  "button-factory": "#9a4f12",
  "civic-theatre": "#007a78",
  "dublin-ie": "#00856a",
  "draiocht": "#8a2f78",
  "entertainment-ie": "#d14900",
  "eventbrite": "#d1410c",
  "evensos": "#1261a0",
  "fringefest": "#c2185b",
  "gaiety-theatre": "#8b1e3f",
  "gate-theatre": "#76520e",
  "mcd": "#006b54",
  "mill-theatre": "#d14a24",
  "national-concert-hall": "#e3146c",
  "pavilion-theatre": "#087f8c",
  "rte-orchestra": "#007749",
  "smock-alley": "#5b4b8a",
  "ticketmaster": "#026cdf",
  "the-academy": "#a63a00",
  "the-grand-social": "#8a3758",
  "tickts": "#226b3a",
  "vicar-street": "#b1365b",
  "whats-on-dublin": "#315c9b",
  "whelans": "#7b5427",
  "workmans-club": "#4e5d35"
};

init();

async function init() {
  initTheme();
  setDefaultDates();
  await loadSources();
  await loadEvents();

  elements.filters.addEventListener("submit", async event => {
    event.preventDefault();
    await loadEvents();
  });

  elements.searchText.addEventListener("input", renderEvents);
  elements.sourceFilter.addEventListener("change", () => {
    updateVenueOptions();
    renderSourceStatus();
    renderEvents();
  });
  elements.venueFilter.addEventListener("change", renderEvents);
  elements.sortOrder.addEventListener("change", renderEvents);
  elements.sourceTabs.forEach(tab => {
    tab.addEventListener("click", () => {
      state.sourceStatusTab = tab.dataset.sourceTab;
      renderSourceStatus();
    });
  });
}

function initTheme() {
  const currentTheme = document.documentElement.dataset.theme ?? "light";
  elements.themeToggle.checked = currentTheme === "dark";

  elements.themeToggle.addEventListener("change", () => {
    const theme = elements.themeToggle.checked ? "dark" : "light";
    document.documentElement.dataset.theme = theme;
    localStorage.setItem("dublin-events-theme", theme);
  });
}

function setDefaultDates() {
  const today = new Date();
  const end = new Date(today);
  end.setDate(today.getDate() + 30);
  elements.fromDate.value = toDateInputValue(today);
  elements.toDate.value = toDateInputValue(end);
}

async function loadSources() {
  const response = await fetch("/api/sources");
  const sources = await response.json();

  for (const source of sources) {
    const option = document.createElement("option");
    option.value = source.name;
    option.textContent = sourceLabels[source.name] ?? source.name;
    elements.sourceFilter.append(option);
  }
}

async function loadEvents() {
  setLoading();
  const params = new URLSearchParams({
    from: elements.fromDate.value,
    to: elements.toDate.value
  });

  try {
    const response = await fetch(`/api/events?${params}`);
    if (!response.ok) {
      throw new Error(`Request failed with ${response.status}`);
    }

    const data = await response.json();
    state.events = data.events ?? [];
    state.sourceResults = data.sourceResults ?? [];
    state.selectedId = state.events[0]?.sourceEventId ?? null;
    updateVenueOptions();
    renderSummary(data);
    renderSourceStatus();
    renderEvents();
  } catch (error) {
    elements.eventList.innerHTML = `<li class="message">Unable to load events. ${escapeHtml(error.message)}</li>`;
    elements.eventDetail.className = "event-detail empty";
    elements.eventDetail.innerHTML = "<p>No event selected</p>";
  }
}

function renderSummary(data) {
  const venues = new Set(state.events.map(event => event.venue).filter(Boolean));
  const activeSources = state.sourceResults.filter(source => source.count > 0).length;

  elements.eventCount.textContent = data.count ?? state.events.length;
  elements.venueCount.textContent = venues.size;
  elements.sourceCount.textContent = activeSources;
  elements.windowLabel.textContent = `${formatShortDate(data.from)} to ${formatShortDate(data.to)}`;
}

function renderSourceStatus() {
  elements.sourceStatus.innerHTML = "";
  updateSourceTabs();

  const visibleSources = state.sourceResults.filter(source =>
    state.sourceStatusTab === "with-events" ? source.count > 0 : source.count === 0);

  if (visibleSources.length === 0) {
    elements.sourceStatus.innerHTML = '<p class="source-empty">No sources in this group.</p>';
    return;
  }

  for (const source of visibleSources) {
    const hasWarnings = source.warnings?.length > 0;
    const isActive = elements.sourceFilter.value === source.source;
    const label = sourceLabels[source.source] ?? source.source;
    const logoUrl = sourceLogos[source.source];
    const node = document.createElement("button");
    node.type = "button";
    node.className = `source-pill ${hasWarnings ? "warn" : "ok"} ${isActive ? "active" : ""} ${logoUrl ? "" : "text-logo"}`;
    node.setAttribute("aria-pressed", String(isActive));
    node.dataset.source = source.source;
    node.innerHTML = `
      <span class="source-brand">
        ${logoUrl ? `<img class="source-logo" src="${escapeAttribute(logoUrl)}" alt="" loading="lazy">` : ""}
        <strong>${escapeHtml(label)}</strong>
      </span>
      <span class="source-count">${source.count} events${hasWarnings ? ` · ${escapeHtml(source.warnings[0])}` : ""}</span>
    `;
    const logo = node.querySelector(".source-logo");
    logo?.addEventListener("error", () => {
      logo.remove();
      node.classList.add("text-logo");
    });
    node.addEventListener("click", () => toggleSourceFilter(source.source));
    elements.sourceStatus.append(node);
  }
}

function updateSourceTabs() {
  const withEvents = state.sourceResults.filter(source => source.count > 0).length;
  const withoutEvents = state.sourceResults.length - withEvents;

  elements.sourcesWithEventsCount.textContent = withEvents;
  elements.sourcesWithoutEventsCount.textContent = withoutEvents;

  elements.sourceTabs.forEach(tab => {
    const isActive = tab.dataset.sourceTab === state.sourceStatusTab;
    tab.classList.toggle("active", isActive);
    tab.setAttribute("aria-selected", String(isActive));
  });
}

function toggleSourceFilter(source) {
  elements.sourceFilter.value = elements.sourceFilter.value === source ? "" : source;
  updateVenueOptions();
  renderSourceStatus();
  renderEvents();
}

function updateVenueOptions() {
  const selectedSource = elements.sourceFilter.value;
  const currentVenue = elements.venueFilter.value;
  const venueCounts = new Map();
  for (const event of state.events) {
    if ((!selectedSource || event.source === selectedSource) && event.venue) {
      venueCounts.set(event.venue, (venueCounts.get(event.venue) ?? 0) + 1);
    }
  }

  const venues = [...venueCounts.keys()].sort((left, right) => compareText(left, right));
  const sourceLabel = sourceLabels[selectedSource] ?? selectedSource;
  elements.venueFilterLabel.textContent = selectedSource ? `${sourceLabel} venue` : "Venue";

  elements.venueFilter.innerHTML = `<option value="">${selectedSource ? `All ${escapeHtml(sourceLabel)} venues` : "All venues"}</option>`;
  for (const venue of venues) {
    const option = document.createElement("option");
    option.value = venue;
    option.textContent = `${venue} (${venueCounts.get(venue)})`;
    elements.venueFilter.append(option);
  }

  elements.venueFilter.value = venues.includes(currentVenue) ? currentVenue : "";
}

function renderEvents() {
  const query = elements.searchText.value.trim().toLowerCase();
  const source = elements.sourceFilter.value;
  const venue = elements.venueFilter.value;
  const filtered = state.events.filter(event => {
    const matchesSource = !source || event.source === source;
    const matchesVenue = !venue || event.venue === venue;
    const searchable = `${event.title} ${event.venue ?? ""} ${event.city ?? ""}`.toLowerCase();
    const matchesQuery = !query || searchable.includes(query);
    return matchesSource && matchesVenue && matchesQuery;
  }).sort(compareEvents);

  elements.visibleCount.textContent = `${filtered.length} shown`;
  elements.eventList.innerHTML = "";

  if (filtered.length === 0) {
    elements.eventList.innerHTML = '<li class="message">No events match the current filters.</li>';
    renderDetail(null);
    return;
  }

  if (!filtered.some(event => event.sourceEventId === state.selectedId)) {
    state.selectedId = filtered[0].sourceEventId;
  }

  for (const event of filtered) {
    const row = document.createElement("li");
    const outsideDublin = isOutsideDublin(event);
    row.className = `event-row ${event.sourceEventId === state.selectedId ? "selected" : ""}`;
    row.style.setProperty("--source-accent", sourceColors[event.source] ?? "var(--admin-blue)");
    row.tabIndex = 0;
    row.innerHTML = `
      <div class="datebox">
        <span>${formatMonth(event.startsAt)}</span>
        <span>${formatDay(event.startsAt)}</span>
      </div>
      <div class="event-main">
        <div class="event-title-line">
          ${sourceLogos[event.source] ? `<img class="event-source-icon" src="${escapeAttribute(sourceLogos[event.source])}" alt="" loading="lazy">` : ""}
          <h3>${escapeHtml(event.title)}</h3>
        </div>
        <div class="event-meta">
          <span>${escapeHtml(formatTime(event.startsAt))}</span>
          <span>${escapeHtml(event.venue ?? "Venue TBC")}</span>
          ${isSoldOut(event) ? '<span class="sold-out-badge">Sold out</span>' : ""}
          ${outsideDublin ? `<span class="location-warning">Outside Dublin: ${escapeHtml(event.city)}</span>` : ""}
          ${event.category ? categoryBadge(event.category) : ""}
        </div>
      </div>
      <div class="event-actions">
        <span class="source-tag">${escapeHtml(sourceLabels[event.source] ?? event.source)}</span>
        <span class="event-share-slot">
          ${isSoldOut(event) ? '<span class="sold-out-action" aria-label="Sold out">Sold out</span>' : `
            <a class="whatsapp-button icon-only" href="${escapeAttribute(buildWhatsAppUrl(event))}" target="_blank" rel="noreferrer" aria-label="Share ${escapeAttribute(event.title)} on WhatsApp" title="Share on WhatsApp" data-share-action>
              ${whatsAppIcon()}
            </a>
          `}
        </span>
      </div>
    `;
    row.addEventListener("click", () => selectEvent(event.sourceEventId));
    row.addEventListener("keydown", keyEvent => {
      if (keyEvent.key === "Enter" || keyEvent.key === " ") {
        keyEvent.preventDefault();
        selectEvent(event.sourceEventId);
      }
    });
    row.querySelector("[data-share-action]")?.addEventListener("click", clickEvent => {
      clickEvent.stopPropagation();
    });
    elements.eventList.append(row);
  }

  renderDetail(filtered.find(event => event.sourceEventId === state.selectedId));
}

function compareEvents(left, right) {
  if (elements.sortOrder.value === "venue") {
    return compareText(left.venue ?? "Venue TBC", right.venue ?? "Venue TBC")
      || compareDate(left.startsAt, right.startsAt)
      || compareText(left.title, right.title);
  }

  return compareDate(left.startsAt, right.startsAt)
    || compareText(left.venue ?? "Venue TBC", right.venue ?? "Venue TBC")
    || compareText(left.title, right.title);
}

function compareDate(left, right) {
  return new Date(left).getTime() - new Date(right).getTime();
}

function compareText(left, right) {
  return left.localeCompare(right, "en-IE", { sensitivity: "base" });
}

function selectEvent(id) {
  state.selectedId = id;
  renderEvents();
}

function renderDetail(event) {
  if (!event) {
    elements.eventDetail.className = "event-detail empty";
    elements.eventDetail.innerHTML = "<p>Select an event</p>";
    return;
  }

  elements.eventDetail.className = "event-detail";
  elements.eventDetail.style.setProperty("--source-accent", sourceColors[event.source] ?? "var(--admin-blue)");
  elements.eventDetail.innerHTML = `
    ${event.imageUrl ? `<img class="detail-image" src="${escapeAttribute(event.imageUrl)}" alt="">` : ""}
    <div class="detail-title-line">
      ${sourceLogos[event.source] ? `<img class="detail-source-icon" src="${escapeAttribute(sourceLogos[event.source])}" alt="">` : ""}
      <h2>${escapeHtml(event.title)}</h2>
    </div>
    <dl class="detail-grid">
      <dt>Date</dt><dd>${escapeHtml(formatLongDate(event.startsAt))}</dd>
      <dt>Time</dt><dd>${escapeHtml(formatTime(event.startsAt))}</dd>
      <dt>Venue</dt><dd>${escapeHtml(event.venue ?? "Venue TBC")}</dd>
      ${isOutsideDublin(event) ? `<dt>Location</dt><dd><span class="location-warning">Outside Dublin: ${escapeHtml(event.city)}</span></dd>` : ""}
      <dt>Source</dt><dd>${escapeHtml(sourceLabels[event.source] ?? event.source)}</dd>
      ${event.category ? `<dt>Category</dt><dd>${categoryBadge(event.category)}</dd>` : ""}
      ${event.status ? `<dt>Status</dt><dd>${isSoldOut(event) ? '<span class="sold-out-badge">Sold out</span>' : escapeHtml(formatStatus(event.status))}</dd>` : ""}
    </dl>
    <div class="detail-actions">
      ${event.url ? `<a class="detail-link" href="${escapeAttribute(event.url)}" target="_blank" rel="noreferrer">Open event</a>` : ""}
      ${isSoldOut(event) ? '<span class="sold-out-action detail-sold-out" aria-label="Sold out">Sold out</span>' : `
        <a class="whatsapp-button" href="${escapeAttribute(buildWhatsAppUrl(event))}" target="_blank" rel="noreferrer">
          ${whatsAppIcon()}
          <span>Share</span>
        </a>
      `}
    </div>
  `;
}

function isOutsideDublin(event) {
  return Boolean(event.city) && !event.city.toLowerCase().includes("dublin");
}

function isSoldOut(event) {
  const status = event.status?.toLowerCase().replace(/[^a-z0-9]/g, "");
  return status?.endsWith("soldout") === true;
}

function formatStatus(status) {
  return status.replaceAll("-", " ").replaceAll("_", " ").replace(/\b\w/g, letter => letter.toUpperCase());
}

function categoryBadge(category) {
  return `<span class="category-badge category-${categoryTone(category)}">${escapeHtml(category)}</span>`;
}

function categoryTone(category) {
  const value = category.toLowerCase();

  if (value.includes("comedy") || value.includes("stand-up")) return "comedy";
  if (value.includes("classical") || value.includes("recital")) return "classical";
  if (value.includes("music") || value.includes("orchestra") || value.includes("traditional") || value.includes("popular")) return "music";
  if (value.includes("theatre") || value.includes("theater") || value.includes("storytelling")) return "theatre";
  if (value.includes("education")) return "education";
  if (value.includes("tour") || value.includes("talk") || value.includes("lecture") || value.includes("reading")) return "talks";
  if (value.includes("dance")) return "dance";
  if (value.includes("family")) return "family";
  if (value.includes("sport")) return "sport";
  if (value.includes("festival")) return "festival";
  if (value.includes("circus")) return "circus";
  return "general";
}

function setLoading() {
  elements.eventList.innerHTML = '<li class="message">Loading events...</li>';
  elements.eventDetail.className = "event-detail empty";
  elements.eventDetail.innerHTML = "<p>Loading</p>";
}

function toDateInputValue(date) {
  return date.toISOString().slice(0, 10);
}

function formatShortDate(value) {
  return new Intl.DateTimeFormat("en-IE", { day: "2-digit", month: "short", year: "numeric" }).format(new Date(value));
}

function formatLongDate(value) {
  return new Intl.DateTimeFormat("en-IE", { weekday: "long", day: "numeric", month: "long", year: "numeric" }).format(new Date(value));
}

function formatMonth(value) {
  return new Intl.DateTimeFormat("en-IE", { month: "short" }).format(new Date(value));
}

function formatDay(value) {
  return new Intl.DateTimeFormat("en-IE", { day: "2-digit" }).format(new Date(value));
}

function formatTime(value) {
  const date = new Date(value);
  if (date.getHours() === 0 && date.getMinutes() === 0) {
    return "Time TBC";
  }

  return new Intl.DateTimeFormat("en-IE", { hour: "2-digit", minute: "2-digit" }).format(date);
}

function buildWhatsAppUrl(event) {
  const parts = [
    event.title,
    event.venue ?? "Venue TBC",
    `${formatLongDate(event.startsAt)} at ${formatTime(event.startsAt)}`,
    event.url
  ].filter(Boolean);

  return `https://wa.me/?text=${encodeURIComponent(parts.join("\n"))}`;
}

function whatsAppIcon() {
  return `
    <svg class="whatsapp-icon" viewBox="0 0 32 32" aria-hidden="true" focusable="false">
      <path d="M16 3.2A12.74 12.74 0 0 0 5 22.35L3.8 28.8l6.57-1.15A12.74 12.74 0 1 0 16 3.2Z" fill="currentColor"/>
      <path d="M22.95 18.93c-.38-.19-2.25-1.11-2.6-1.24-.35-.13-.6-.19-.86.19-.25.37-.99 1.23-1.21 1.48-.22.25-.44.28-.82.09-.38-.19-1.6-.59-3.05-1.88a11.4 11.4 0 0 1-2.1-2.62c-.22-.38-.02-.58.17-.77.17-.17.38-.44.57-.66.19-.22.25-.38.38-.63.13-.25.06-.47-.03-.66-.09-.19-.85-2.04-1.17-2.8-.31-.74-.62-.64-.85-.65h-.73c-.25 0-.66.09-1 .47-.35.38-1.32 1.29-1.32 3.15s1.35 3.65 1.54 3.9c.19.25 2.66 4.06 6.44 5.69.9.39 1.6.62 2.15.79.9.29 1.72.25 2.37.15.72-.11 2.25-.92 2.57-1.81.32-.89.32-1.65.22-1.81-.09-.16-.35-.25-.73-.44Z" fill="#fff"/>
    </svg>
  `;
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

function escapeAttribute(value) {
  return escapeHtml(value).replaceAll("`", "&#096;");
}
