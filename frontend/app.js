// Backend base URL — skift den her når vi deployer.
const API_BASE = 'http://localhost:5070';

// Accent-farver — spejlet i style.css for verdict-kortene.
const COLORS = {
    yr: '#f5a623',
    dmi: '#3b9eff',
    observed: '#3ecf8e'
};

// Dark-theme tokens, brugt af Chart.js options nedenfor.
const GRID = '#262c38';
const MUTED = '#8b909c';
const TEXT = '#e8eaed';
const CARD_BG = '#1a1f29';

// Chart.js globale defaults — alle charts arver dem.
Chart.defaults.color = MUTED;
Chart.defaults.borderColor = GRID;
Chart.defaults.font.family =
    '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif';

let currentDays = 7;
const charts = { tomorrow: null, history: null };

// Guess-state: seneste comparison-data + brugerens gæt. Gættet låses for hele
// side-loadet — togglen skifter kun datavinduet, ikke gæt-tilstanden.
let comparisonData = null;
let userGuess = null; // 'Yr' | 'DMI' | null
let hasGuessed = false;

const btn7 = document.getElementById('btn-7');
const btn30 = document.getElementById('btn-30');

btn7.addEventListener('click', () => setDays(7));
btn30.addEventListener('click', () => setDays(30));

function setDays(n) {
    currentDays = n;
    updateToggle();
    // Togglen ændrer KUN datavinduet — gættet er stadig låst hvis brugeren har gættet.
    // Tomorrow afhænger ikke af days — refresh kun de to der gør.
    loadComparison(n);
    loadHistory(n);
}

async function onGuess(provider) {
    if (hasGuessed) return; // 1 gæt per side-load.
    hasGuessed = true;
    userGuess = provider;
    const hero = document.querySelector('.hero');
    hero.classList.remove('awaiting-guess');
    hero.classList.add('guessed'); // skjuler klik-zonerne permanent
    document.getElementById('verdict-section').classList.add('revealed');

    // Vent kort på data hvis fetch endnu ikke er færdig. Typisk er den der
    // allerede når brugeren har læst prompten og klikket.
    if (!comparisonData) await waitForComparisonData(2500);

    if (comparisonData && comparisonData.mostAccurate) {
        // Skift hero-billedet UNDER overlayet — så når overlayet lukker, ligger
        // winner-billedet allerede og venter uden synligt blink.
        applyWinnerImage(comparisonData.mostAccurate);
        await fullscreenReveal();
    }

    if (comparisonData) renderVerdict();
}

function waitForComparisonData(maxMs) {
    return new Promise(resolve => {
        const start = Date.now();
        const tick = () => {
            if (comparisonData || Date.now() - start >= maxMs) resolve();
            else setTimeout(tick, 50);
        };
        tick();
    });
}

function fullscreenReveal() {
    return new Promise(resolve => {
        const overlay = document.getElementById('fullscreen-reveal');
        const img = document.getElementById('reveal-img');
        const popup = document.getElementById('reveal-popup');

        const winner = comparisonData.mostAccurate;
        const correct = userGuess === winner;

        img.src = winner === 'Yr'
            ? 'pictures/hero_YR_Winner.png'
            : 'pictures/hero_DMI_Winner.png';

        popup.className = 'reveal-popup ' + (correct ? 'correct' : 'wrong');
        popup.innerHTML = correct
            ? `<div class="reveal-headline">🐐 GOAT!</div>
               <div class="reveal-sub">Du havde ret.</div>`
            : `<div class="reveal-headline">😬 Du tog fejl</div>
               <div class="reveal-sub">Din taber.</div>`;

        overlay.classList.add('visible');

        // 2 sekunder fuldskærm, så fade ud. Vent på fade-out før vi resolver,
        // ellers kører renderVerdict midt i animationen.
        setTimeout(() => {
            overlay.classList.remove('visible');
            setTimeout(resolve, 380);
        }, 3000);
    });
}

function applyWinnerImage(mostAccurate) {
    const hero = document.querySelector('.hero');
    hero.classList.remove('yr-won', 'dmi-won');
    if (mostAccurate === 'Yr') hero.classList.add('yr-won');
    else if (mostAccurate === 'DMI') hero.classList.add('dmi-won');
    // Hvis mostAccurate er null (tom data), beholder vi default-billedet.
}

function updateToggle() {
    btn7.classList.toggle('active', currentDays === 7);
    btn30.classList.toggle('active', currentDays === 30);
}

async function fetchJson(url) {
    const res = await fetch(url);
    if (!res.ok) throw new Error(`${res.status} ${res.statusText}`);
    return res.json();
}

function showError(bodyEl) {
    bodyEl.innerHTML =
        `<div class="section-error">Couldn't load — is the backend running on ${API_BASE}?</div>`;
}

// =========================================================================
// SECTION 1 — VERDICT
// =========================================================================

async function loadComparison(days) {
    const body = document.getElementById('verdict-body');
    document.getElementById('verdict-window').textContent = `· last ${days} days`;
    body.innerHTML = '<div class="loading">Loading…</div>';
    comparisonData = null;

    try {
        comparisonData = await fetchJson(
            `${API_BASE}/api/WeatherForecast/comparison?days=${days}`
        );
        renderVerdict();
    } catch (err) {
        showError(body);
    }
}

function renderVerdict() {
    if (!comparisonData) return;

    const body = document.getElementById('verdict-body');
    const { mostAccurate, summary, days } = comparisonData;

    // Skift hero-billedet til winner-versionen (eller default ved tom data).
    // Køres her så billedet også opdaterer hvis brugeren toggler 7↔30 efter gæt.
    if (hasGuessed) applyWinnerImage(mostAccurate);

    // Result-banner indsættes øverst hvis brugeren har gættet OG vi har et resultat.
    // Tom-data-tilfældet falder igennem til empty-state nedenfor — uden right/wrong.
    let resultHtml = '';
    if (userGuess && mostAccurate) {
        const correct = userGuess === mostAccurate;
        resultHtml = correct
            ? `<div class="guess-result correct">✅ Correct! ${mostAccurate} was most accurate.</div>`
            : `<div class="guess-result wrong">❌ Not quite — ${mostAccurate} was actually most accurate.</div>`;
    }

    if (!mostAccurate || !summary || summary.length === 0) {
        body.innerHTML = `
            ${resultHtml}
            <div class="empty-state">
                Not enough overlapping data yet — results will appear as the daily collection runs.
            </div>
        `;
        return;
    }

    // Find Yr og DMI i summary — defensivt, begge er ikke garanteret tilstede.
    const yr = summary.find(s => s.provider === 'Yr');
    const dmi = summary.find(s => s.provider === 'DMI');
    const winnerClass = mostAccurate.toLowerCase();

    const cardHtml = (entry, isWinner) => {
        if (!entry) return '';
        const cls = entry.provider.toLowerCase();
        return `
            <div class="metric-card ${cls}${isWinner ? ' winner' : ''}">
                ${isWinner ? '<span class="winner-badge">Most accurate</span>' : ''}
                <div class="metric-header">
                    <span class="dot dot-${cls}"></span>
                    <span class="provider-name ${cls}">${entry.provider}</span>
                </div>
                <div class="metric-value">${entry.overallMae.toFixed(2)}<span class="unit">°C</span></div>
                <div class="metric-label">Mean absolute error</div>
                <div class="metric-sub">based on ${entry.totalHoursMatched} matched hours</div>
            </div>
        `;
    };

    body.innerHTML = `
        ${resultHtml}
        <p class="verdict-line">
            <span class="winner-name ${winnerClass}">${mostAccurate}</span>
            is more accurate over the last ${days} days
        </p>
        <div class="metric-grid">
            ${cardHtml(yr, mostAccurate === 'Yr')}
            ${cardHtml(dmi, mostAccurate === 'DMI')}
        </div>
        <p class="lower-better">Lower MAE is better.</p>
    `;
}

// =========================================================================
// SECTION 2 — NEXT 24 HOURS
// =========================================================================

async function loadTomorrow() {
    const body = document.getElementById('tomorrow-body');
    destroyChart('tomorrow');
    body.innerHTML = '<div class="loading">Loading…</div>';

    try {
        const data = await fetchJson(`${API_BASE}/api/WeatherForecast/tomorrow`);
        renderTomorrow(data);
    } catch (err) {
        showError(body);
    }
}

function renderTomorrow(rows) {
    const body = document.getElementById('tomorrow-body');

    if (!rows || rows.length === 0) {
        body.innerHTML = '<div class="empty-state">No forecast data for tomorrow yet.</div>';
        return;
    }

    // /tomorrow er en flad liste — gruppér til to serier på en fælles tids-akse.
    const allTimes = [...new Set(rows.map(r => r.targetDateTime))].sort();
    const yrByTime = {};
    const dmiByTime = {};
    for (const r of rows) {
        if (r.provider === 'Yr') yrByTime[r.targetDateTime] = r.predTemp;
        else if (r.provider === 'DMI') dmiByTime[r.targetDateTime] = r.predTemp;
    }

    body.innerHTML = `
        <div class="chart-wrap"><canvas id="chart-tomorrow"></canvas></div>
        <p class="chart-note" id="tomorrow-note"></p>
    `;

    const labels = allTimes.map(formatHourLabel);
    const yrData = allTimes.map(t => yrByTime[t] ?? null);
    const dmiData = allTimes.map(t => dmiByTime[t] ?? null);

    charts.tomorrow = new Chart(document.getElementById('chart-tomorrow'), {
        type: 'line',
        data: {
            labels,
            datasets: [
                lineDataset('Yr', yrData, COLORS.yr),
                lineDataset('DMI', dmiData, COLORS.dmi)
            ]
        },
        options: chartOptions('°C')
    });

    // Største uenighed — kun timer hvor begge providers har en værdi.
    const gap = biggestGap(yrByTime, dmiByTime);
    if (gap) {
        document.getElementById('tomorrow-note').textContent =
            `Largest disagreement: ${gap.diff.toFixed(1)} °C at ${formatHourLabel(gap.time)} UTC.`;
    }
}

function biggestGap(yrByTime, dmiByTime) {
    let best = null;
    for (const t of Object.keys(yrByTime)) {
        if (dmiByTime[t] === undefined) continue;
        const diff = Math.abs(yrByTime[t] - dmiByTime[t]);
        if (!best || diff > best.diff) best = { diff, time: t };
    }
    return best;
}

// =========================================================================
// SECTION 3 — HISTORY
// =========================================================================

async function loadHistory(days) {
    const body = document.getElementById('history-body');
    document.getElementById('history-window').textContent = `· last ${days} days`;
    destroyChart('history');
    body.innerHTML = '<div class="loading">Loading…</div>';

    try {
        const data = await fetchJson(
            `${API_BASE}/api/WeatherForecast/timeseries?days=${days}`
        );
        renderHistory(data);
    } catch (err) {
        showError(body);
    }
}

function renderHistory(data) {
    const body = document.getElementById('history-body');
    const points = (data && data.points) || [];

    if (points.length === 0) {
        body.innerHTML = '<div class="empty-state">No history data in this window yet.</div>';
        return;
    }

    body.innerHTML = '<div class="chart-wrap"><canvas id="chart-history"></canvas></div>';

    const labels = points.map(p => formatDateHourLabel(p.time));
    const yr = points.map(p => p.yr);
    const dmi = points.map(p => p.dmi);
    const obs = points.map(p => p.observed);

    charts.history = new Chart(document.getElementById('chart-history'), {
        type: 'line',
        data: {
            labels,
            datasets: [
                lineDataset('Yr', yr, COLORS.yr),
                lineDataset('DMI', dmi, COLORS.dmi),
                lineDataset('Observed', obs, COLORS.observed)
            ]
        },
        options: chartOptions('°C')
    });
}

// =========================================================================
// Chart helpers
// =========================================================================

function lineDataset(label, data, color) {
    return {
        label,
        data,
        borderColor: color,
        backgroundColor: color,
        pointRadius: 0,
        pointHoverRadius: 4,
        borderWidth: 2,
        tension: 0.25,
        // spanGaps: false → null'er bliver til gaps i linjen, hvilket vi vil have.
        spanGaps: false
    };
}

function chartOptions(yLabel) {
    return {
        responsive: true,
        maintainAspectRatio: false,
        interaction: { mode: 'index', intersect: false },
        plugins: {
            legend: {
                position: 'top',
                labels: {
                    color: TEXT,
                    usePointStyle: true,
                    pointStyle: 'circle',
                    boxWidth: 8,
                    padding: 16
                }
            },
            tooltip: {
                backgroundColor: CARD_BG,
                titleColor: TEXT,
                bodyColor: TEXT,
                borderColor: GRID,
                borderWidth: 1,
                padding: 10,
                callbacks: {
                    label: ctx => ctx.parsed.y == null
                        ? `${ctx.dataset.label}: —`
                        : `${ctx.dataset.label}: ${ctx.parsed.y} °C`
                }
            }
        },
        scales: {
            x: {
                grid: { color: GRID, drawTicks: false },
                ticks: { color: MUTED, autoSkip: true, maxTicksLimit: 10, maxRotation: 0 }
            },
            y: {
                grid: { color: GRID, drawTicks: false },
                ticks: { color: MUTED },
                title: { display: true, text: yLabel, color: MUTED }
            }
        }
    };
}

function destroyChart(name) {
    if (charts[name]) {
        charts[name].destroy();
        charts[name] = null;
    }
}

// =========================================================================
// Time formatters (UTC)
// =========================================================================

function formatHourLabel(iso) {
    const d = new Date(iso);
    const hh = String(d.getUTCHours()).padStart(2, '0');
    const mi = String(d.getUTCMinutes()).padStart(2, '0');
    return `${hh}:${mi}`;
}

function formatDateHourLabel(iso) {
    const d = new Date(iso);
    const mm = String(d.getUTCMonth() + 1).padStart(2, '0');
    const dd = String(d.getUTCDate()).padStart(2, '0');
    const hh = String(d.getUTCHours()).padStart(2, '0');
    return `${mm}-${dd} ${hh}:00`;
}

// =========================================================================
// Initial load — 7 days default. Alle tre sektioner kører uafhængigt,
// så en fejl i én sektion ikke vælter de andre.
// =========================================================================

// Hero click-zoner: hver knap har data-provider="Yr" / "DMI".
document.querySelectorAll('.hero-zone').forEach(btn => {
    btn.addEventListener('click', () => onGuess(btn.dataset.provider));
});

updateToggle();
loadComparison(currentDays);
loadTomorrow();
loadHistory(currentDays);
