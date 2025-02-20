// Incident Dashboard JavaScript

let trendsChart;
let mitigationChart;

async function fetchDashboardData() {
    try {
        const response = await fetch('/api/incidents/analysis');
        const data = await response.json();
        updateDashboard(data);
    } catch (error) {
        console.error('Error fetching dashboard data:', error);
    }
}

function updateDashboard(data) {
    updateSystemMetrics(data.systemMetrics);
    updateRecentIncidents(data.recentIncidents);
    updateTrendsChart(data.patterns);
    updateMitigationChart(data.systemMetrics);
    updatePatterns(data.patterns);
    updateRecommendations(data.recommendations);
}

function updateSystemMetrics(metrics) {
    document.getElementById('mtbf').textContent = formatDuration(metrics.mtbf);
    document.getElementById('mttr').textContent = formatDuration(metrics.mttr);
    document.getElementById('availability').textContent = 
        metrics.availability.toFixed(2) + '%';
}

function updateRecentIncidents(incidents) {
    const list = document.getElementById('incidentsList');
    list.innerHTML = incidents.map(incident => `
        <div class="incident-item severity-${incident.severity.toLowerCase()}">
            <div class="incident-header">
                <span class="timestamp">${formatDate(incident.timestamp)}</span>
                <span class="severity">${incident.severity}</span>
            </div>
            <div class="description">${incident.description}</div>
            <div class="metadata">
                Category: ${incident.category} | Affected Users: ${incident.affectedUsers}
            </div>
        </div>
    `).join('');
}

function updateTrendsChart(patterns) {
    const ctx = document.getElementById('trendsChart').getContext('2d');
    
    if (trendsChart) {
        trendsChart.destroy();
    }

    const categories = [...new Set(patterns.map(p => p.category))];
    const datasets = categories.map(category => {
        const categoryPatterns = patterns.filter(p => p.category === category);
        return {
            label: category,
            data: categoryPatterns.map(p => p.frequency),
            fill: false
        };
    });

    trendsChart = new Chart(ctx, {
        type: 'line',
        data: {
            labels: patterns.map(p => formatDate(p.lastOccurrence)),
            datasets: datasets
        },
        options: {
            responsive: true,
            scales: {
                y: {
                    beginAtZero: true
                }
            }
        }
    });
}

function updateMitigationChart(metrics) {
    const ctx = document.getElementById('mitigationChart').getContext('2d');
    
    if (mitigationChart) {
        mitigationChart.destroy();
    }

    mitigationChart = new Chart(ctx, {
        type: 'doughnut',
        data: {
            labels: ['Successful', 'Failed'],
            datasets: [{
                data: [
                    metrics.successfulMitigations,
                    metrics.totalIncidents - metrics.successfulMitigations
                ],
                backgroundColor: ['#4CAF50', '#F44336']
            }]
        },
        options: {
            responsive: true
        }
    });
}

function updatePatterns(patterns) {
    const list = document.getElementById('patternsList');
    list.innerHTML = patterns.map(pattern => `
        <div class="pattern-item">
            <h4>${pattern.category}</h4>
            <p>Frequency: ${pattern.frequency}</p>
            <p>Average Recovery Time: ${formatDuration(pattern.avgRecoveryTime)}</p>
            <p>Last Occurrence: ${formatDate(pattern.lastOccurrence)}</p>
        </div>
    `).join('');
}

function updateRecommendations(recommendations) {
    const list = document.getElementById('recommendationsList');
    list.innerHTML = recommendations.map(rec => `
        <div class="recommendation-item priority-${rec.priority}">
            <div class="priority-indicator">Priority: ${rec.priority}</div>
            <div class="pattern-info">
                <strong>Pattern:</strong> ${rec.pattern.category}
            </div>
            <div class="actions">
                <strong>Suggested Actions:</strong>
                <ul>
                    ${rec.suggestedActions.map(action => `<li>${action}</li>`).join('')}
                </ul>
            </div>
            <div class="impact">
                Estimated Impact: ${(rec.estimatedImpact * 100).toFixed(1)}%
            </div>
        </div>
    `).join('');
}

function formatDate(dateString) {
    return new Date(dateString).toLocaleString();
}

function formatDuration(duration) {
    const parts = duration.split(':');
    const hours = parseInt(parts[0]);
    const minutes = parseInt(parts[1]);
    return `${hours}h ${minutes}m`;
}

// Initialize dashboard
document.addEventListener('DOMContentLoaded', () => {
    fetchDashboardData();
    // Refresh data every 5 minutes
    setInterval(fetchDashboardData, 5 * 60 * 1000);
});