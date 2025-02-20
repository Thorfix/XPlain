document.addEventListener('DOMContentLoaded', function() {
    const alertHub = new signalR.HubConnectionBuilder()
        .withUrl("/alertHub")
        .configureLogging(signalR.LogLevel.Information)
        .build();

    let alertTrendChart = null;

    async function initialize() {
        await connectToHub();
        await loadInitialData();
        setupEventHandlers();
        initializeCharts();
    }

    async function connectToHub() {
        try {
            await alertHub.start();
            console.log("Connected to Alert Hub");
            
            alertHub.on("AlertReceived", (alert) => {
                updateAlertList(alert);
                updateAlertStats();
                updateTrendChart();
            });
        } catch (err) {
            console.error("Error connecting to hub:", err);
            setTimeout(connectToHub, 5000);
        }
    }

    async function loadInitialData() {
        try {
            const response = await fetch('/api/alerts');
            const alerts = await response.json();
            displayAlerts(alerts);
            updateAlertStats();
        } catch (err) {
            console.error("Error loading initial data:", err);
        }
    }

    function setupEventHandlers() {
        document.getElementById('severity-filter').addEventListener('change', filterAlerts);
        document.getElementById('status-filter').addEventListener('change', filterAlerts);
        document.getElementById('date-filter').addEventListener('change', filterAlerts);
    }

    function initializeCharts() {
        const ctx = document.getElementById('alert-trend-chart').getContext('2d');
        alertTrendChart = new Chart(ctx, {
            type: 'line',
            data: {
                labels: [],
                datasets: [{
                    label: 'Critical Alerts',
                    borderColor: 'rgb(255, 99, 132)',
                    data: []
                }, {
                    label: 'Warning Alerts',
                    borderColor: 'rgb(255, 205, 86)',
                    data: []
                }, {
                    label: 'Info Alerts',
                    borderColor: 'rgb(54, 162, 235)',
                    data: []
                }]
            },
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
                        beginAtZero: true
                    }
                }
            }
        });
    }

    function updateAlertList(alert) {
        const alertElement = createAlertElement(alert);
        const container = document.getElementById('active-alerts');
        container.insertBefore(alertElement, container.firstChild);
    }

    function createAlertElement(alert) {
        const div = document.createElement('div');
        div.className = `alert-item ${alert.severity.toLowerCase()}`;
        div.innerHTML = `
            <div class="alert-header">
                <span class="alert-title">${alert.title}</span>
                <span class="alert-time">${new Date(alert.createdAt).toLocaleString()}</span>
            </div>
            <div class="alert-description">${alert.description}</div>
            <div class="alert-footer">
                <span class="alert-severity">${alert.severity}</span>
                <span class="alert-source">${alert.source}</span>
                ${alert.status === 'New' ? createActionButtons(alert.id) : ''}
            </div>
        `;
        return div;
    }

    function createActionButtons(alertId) {
        return `
            <div class="alert-actions">
                <button onclick="acknowledgeAlert('${alertId}')">Acknowledge</button>
                <button onclick="resolveAlert('${alertId}')">Resolve</button>
            </div>
        `;
    }

    async function acknowledgeAlert(alertId) {
        try {
            await fetch(`/api/alerts/${alertId}/acknowledge`, {
                method: 'POST'
            });
            await loadInitialData();
        } catch (err) {
            console.error("Error acknowledging alert:", err);
        }
    }

    async function resolveAlert(alertId) {
        try {
            await fetch(`/api/alerts/${alertId}/resolve`, {
                method: 'POST'
            });
            await loadInitialData();
        } catch (err) {
            console.error("Error resolving alert:", err);
        }
    }

    async function updateAlertStats() {
        try {
            const response = await fetch('/api/alerts/stats');
            const stats = await response.json();
            
            document.getElementById('active-alerts-count').textContent = stats.activeCount;
            document.getElementById('critical-alerts-count').textContent = stats.criticalCount;
            document.getElementById('resolved-today-count').textContent = stats.resolvedTodayCount;
        } catch (err) {
            console.error("Error updating alert stats:", err);
        }
    }

    async function updateTrendChart() {
        try {
            const response = await fetch('/api/alerts/trends');
            const trends = await response.json();
            
            alertTrendChart.data.labels = trends.timestamps;
            alertTrendChart.data.datasets[0].data = trends.criticalCounts;
            alertTrendChart.data.datasets[1].data = trends.warningCounts;
            alertTrendChart.data.datasets[2].data = trends.infoCounts;
            alertTrendChart.update();
        } catch (err) {
            console.error("Error updating trend chart:", err);
        }
    }

    async function filterAlerts() {
        const severity = document.getElementById('severity-filter').value;
        const status = document.getElementById('status-filter').value;
        const date = document.getElementById('date-filter').value;

        try {
            const response = await fetch(`/api/alerts/filter?severity=${severity}&status=${status}&date=${date}`);
            const alerts = await response.json();
            displayAlerts(alerts);
        } catch (err) {
            console.error("Error filtering alerts:", err);
        }
    }

    function displayAlerts(alerts) {
        const container = document.getElementById('active-alerts');
        container.innerHTML = '';
        alerts.forEach(alert => {
            container.appendChild(createAlertElement(alert));
        });
    }

    // Initialize the dashboard
    initialize();
});