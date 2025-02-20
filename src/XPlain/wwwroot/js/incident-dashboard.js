// Incident Dashboard JavaScript

let trendsChart;
let mitigationChart;
let timelineChart;
let notificationSocket;

// Store filtered data
let currentData = {
    incidents: [],
    patterns: [],
    recommendations: [],
    metrics: {}
};

async function fetchDashboardData(filters = {}) {
    try {
        const queryParams = new URLSearchParams(filters).toString();
        const response = await fetch(`/api/incidents/analysis?${queryParams}`);
        const data = await response.json();
        currentData = data;
        updateDashboard(data);
    } catch (error) {
        console.error('Error fetching dashboard data:', error);
        showNotification('Error loading dashboard data', 'error');
    }
}

function initializeWebSocket() {
    notificationSocket = new WebSocket(`ws://${window.location.host}/incidents/notifications`);
    
    notificationSocket.onmessage = (event) => {
        const notification = JSON.parse(event.data);
        if (notification.severity === 'Critical') {
            showNotification(notification.message, 'critical');
            playAlertSound();
        }
        // Refresh dashboard data for real-time updates
        fetchDashboardData();
    };

    notificationSocket.onerror = (error) => {
        console.error('WebSocket error:', error);
    };
}

function showNotification(message, type) {
    const panel = document.getElementById('notificationPanel');
    const content = panel.querySelector('.notification-content');
    
    content.textContent = message;
    panel.className = `notification-panel ${type}`;
    panel.classList.remove('hidden');
    
    if (type !== 'critical') {
        setTimeout(() => panel.classList.add('hidden'), 5000);
    }
}

function updateDashboard(data) {
    updateSystemMetrics(data.systemMetrics);
    updateRecentIncidents(data.recentIncidents);
    updateTrendsChart(data.patterns);
    updateMitigationChart(data.systemMetrics);
    updatePatterns(data.patterns);
    updateRecommendations(data.recommendations);
    updateTimeline(data.incidents, data.mitigations);
    updateFilterOptions(data);
}

function updateTimeline(incidents, mitigations) {
    const ctx = document.getElementById('timelineView').getContext('2d');
    
    if (timelineChart) {
        timelineChart.destroy();
    }

    const timelineData = {
        datasets: [
            {
                label: 'Incidents',
                data: incidents.map(inc => ({
                    x: new Date(inc.timestamp),
                    y: inc.severity === 'Critical' ? 1 : 0.5,
                    incident: inc
                })),
                pointStyle: 'circle',
                pointRadius: 8,
                pointBackgroundColor: incident => 
                    incident.severity === 'Critical' ? '#ff0000' : '#ffa500'
            },
            {
                label: 'Mitigations',
                data: mitigations.map(mit => ({
                    x: new Date(mit.timestamp),
                    y: 0.75,
                    mitigation: mit
                })),
                pointStyle: 'triangle',
                pointRadius: 8,
                pointBackgroundColor: '#00ff00'
            }
        ]
    };

    timelineChart = new Chart(ctx, {
        type: 'scatter',
        data: timelineData,
        options: {
            responsive: true,
            scales: {
                x: {
                    type: 'time',
                    time: {
                        unit: 'hour'
                    }
                },
                y: {
                    display: false
                }
            },
            onClick: (event, elements) => {
                if (elements.length > 0) {
                    const element = elements[0];
                    const data = element.dataset.data[element.index];
                    if (data.incident) {
                        showIncidentDetails(data.incident);
                    }
                }
            }
        }
    });
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

function exportToPDF() {
    const { jsPDF } = window.jspdf;
    const doc = new jsPDF();
    
    // Add title
    doc.setFontSize(18);
    doc.text('Incident Analysis Report', 20, 20);
    
    // Add system metrics
    doc.setFontSize(12);
    doc.text(`MTBF: ${currentData.systemMetrics.mtbf}`, 20, 40);
    doc.text(`MTTR: ${currentData.systemMetrics.mttr}`, 20, 50);
    doc.text(`Availability: ${currentData.systemMetrics.availability}%`, 20, 60);
    
    // Add patterns and recommendations
    doc.text('Key Patterns:', 20, 80);
    currentData.patterns.forEach((pattern, index) => {
        doc.text(`${pattern.category}: ${pattern.frequency} occurrences`, 30, 90 + (index * 10));
    });
    
    // Save the PDF
    doc.save('incident-report.pdf');
}

function exportToCSV() {
    const data = currentData.incidents.map(incident => ({
        Timestamp: incident.timestamp,
        Severity: incident.severity,
        Category: incident.category,
        Description: incident.description,
        AffectedUsers: incident.affectedUsers,
        Resolution: incident.resolution
    }));
    
    const ws = XLSX.utils.json_to_sheet(data);
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, "Incidents");
    XLSX.writeFile(wb, 'incident-report.xlsx');
}

function showIncidentDetails(incident) {
    const modal = document.getElementById('detailsModal');
    const details = document.getElementById('incidentDetails');
    const related = document.getElementById('relatedIncidents');
    const mitigations = document.getElementById('mitigationHistory');
    
    details.innerHTML = `
        <h4>${incident.category}</h4>
        <p><strong>Time:</strong> ${formatDate(incident.timestamp)}</p>
        <p><strong>Severity:</strong> ${incident.severity}</p>
        <p><strong>Description:</strong> ${incident.description}</p>
        <p><strong>Affected Users:</strong> ${incident.affectedUsers}</p>
        <p><strong>Resolution:</strong> ${incident.resolution || 'Pending'}</p>
    `;
    
    // Load related incidents
    fetch(`/api/incidents/related/${incident.id}`)
        .then(response => response.json())
        .then(data => {
            related.innerHTML = `
                <h4>Related Incidents</h4>
                ${data.map(inc => `
                    <div class="related-incident">
                        <span>${formatDate(inc.timestamp)}</span>
                        <span>${inc.category}</span>
                        <span>${inc.severity}</span>
                    </div>
                `).join('')}
            `;
        });
    
    // Load mitigation history
    fetch(`/api/incidents/mitigations/${incident.id}`)
        .then(response => response.json())
        .then(data => {
            mitigations.innerHTML = `
                <h4>Mitigation History</h4>
                ${data.map(mit => `
                    <div class="mitigation-item">
                        <span>${formatDate(mit.timestamp)}</span>
                        <span>${mit.action}</span>
                        <span>Success: ${mit.successful ? 'Yes' : 'No'}</span>
                    </div>
                `).join('')}
            `;
        });
    
    modal.classList.remove('hidden');
}

// Initialize dashboard
document.addEventListener('DOMContentLoaded', () => {
    // Initialize WebSocket for real-time notifications
    initializeWebSocket();
    
    // Initialize filters
    document.getElementById('categoryFilter').addEventListener('change', handleFilterChange);
    document.getElementById('severityFilter').addEventListener('change', handleFilterChange);
    document.getElementById('dateFilter').addEventListener('change', handleFilterChange);
    
    // Initialize export buttons
    document.getElementById('exportPDF').addEventListener('click', exportToPDF);
    document.getElementById('exportCSV').addEventListener('click', exportToCSV);
    
    // Initialize timeline controls
    document.getElementById('zoomIn').addEventListener('click', () => handleTimelineZoom('in'));
    document.getElementById('zoomOut').addEventListener('click', () => handleTimelineZoom('out'));
    document.getElementById('timelineRange').addEventListener('change', handleTimelineRangeChange);
    
    // Initialize modal close buttons
    document.querySelectorAll('.close-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            btn.closest('.modal, .notification-panel').classList.add('hidden');
        });
    });
    
    // Initial data load
    fetchDashboardData();
    
    // Refresh data every 5 minutes
    setInterval(fetchDashboardData, 5 * 60 * 1000);
});