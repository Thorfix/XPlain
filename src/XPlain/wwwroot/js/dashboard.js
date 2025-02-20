// Cache Monitoring Dashboard
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/cacheHub")
    .build();

let healthChart = null;
let metricsChart = null;

async function initializeDashboard() {
    await loadHealthStatus();
    await loadMetrics();
    await loadAlerts();
    await loadAnalytics();
    setupRefreshIntervals();
}

async function loadHealthStatus() {
    const response = await fetch('/api/cache/health');
    const health = await response.json();
    
    const ctx = document.getElementById('healthChart').getContext('2d');
    healthChart = new Chart(ctx, {
        type: 'gauge',
        data: {
            datasets: [{
                value: health.hitRatio * 100,
                data: [20, 40, 60, 80, 100],
                backgroundColor: ['red', 'orange', 'yellow', 'lightgreen', 'green']
            }]
        },
        options: {
            title: {
                display: true,
                text: 'Cache Health'
            }
        }
    });

    updateHealthIndicators(health);
}

async function loadMetrics() {
    const response = await fetch('/api/cache/metrics');
    const metrics = await response.json();
    
    const ctx = document.getElementById('metricsChart').getContext('2d');
    metricsChart = new Chart(ctx, {
        type: 'line',
        data: {
            labels: metrics.timestamps,
            datasets: [{
                label: 'Response Time (ms)',
                data: metrics.responseTimes
            }, {
                label: 'Memory Usage (MB)',
                data: metrics.memoryUsage
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false
        }
    });
}

async function loadAlerts() {
    const response = await fetch('/api/cache/alerts');
    const alerts = await response.json();
    
    const alertsContainer = document.getElementById('alertsList');
    alertsContainer.innerHTML = '';
    
    alerts.forEach(alert => {
        const alertElement = document.createElement('div');
        alertElement.className = `alert alert-${getSeverityClass(alert.severity)}`;
        alertElement.innerHTML = `
            <strong>${alert.type}</strong>
            <p>${alert.message}</p>
            <small>${new Date(alert.timestamp).toLocaleString()}</small>
        `;
        alertsContainer.appendChild(alertElement);
    });
}

async function loadAnalytics() {
    const response = await fetch('/api/cache/analytics/7'); // Last 7 days
    const analytics = await response.json();
    
    const ctx = document.getElementById('analyticsChart').getContext('2d');
    new Chart(ctx, {
        type: 'bar',
        data: {
            labels: analytics.map(a => new Date(a.timestamp).toLocaleDateString()),
            datasets: [{
                label: 'Hit Rate',
                data: analytics.map(a => a.hitRate * 100)
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            scales: {
                y: {
                    beginAtZero: true,
                    max: 100
                }
            }
        }
    });
}

function updateHealthIndicators(health) {
    document.getElementById('hitRatio').textContent = `${(health.hitRatio * 100).toFixed(1)}%`;
    document.getElementById('memoryUsage').textContent = `${health.memoryUsageMB.toFixed(1)} MB`;
    document.getElementById('responseTime').textContent = `${health.averageResponseTimeMs.toFixed(1)} ms`;
    document.getElementById('itemCount').textContent = health.cachedItemCount;
    document.getElementById('lastUpdate').textContent = new Date(health.lastUpdate).toLocaleString();
}

function getSeverityClass(severity) {
    switch (severity.toLowerCase()) {
        case 'critical': return 'danger';
        case 'warning': return 'warning';
        case 'info': return 'info';
        default: return 'secondary';
    }
}

function setupRefreshIntervals() {
    // Refresh health status every minute
    setInterval(loadHealthStatus, 60000);
    
    // Refresh metrics every 5 minutes
    setInterval(loadMetrics, 300000);
    
    // Refresh alerts every 30 seconds
    setInterval(loadAlerts, 30000);
    
    // Refresh analytics every hour
    setInterval(loadAnalytics, 3600000);
}

// SignalR real-time updates
connection.on("HealthUpdate", (health) => {
    updateHealthIndicators(health);
    if (healthChart) {
        healthChart.data.datasets[0].value = health.hitRatio * 100;
        healthChart.update();
    }
});

connection.on("MetricsUpdate", (metrics) => {
    if (metricsChart) {
        metricsChart.data.labels = metrics.timestamps;
        metricsChart.data.datasets[0].data = metrics.responseTimes;
        metricsChart.data.datasets[1].data = metrics.memoryUsage;
        metricsChart.update();
    }
});

connection.start().then(() => {
    console.log("Connected to SignalR hub");
    initializeDashboard();
}).catch(err => console.error(err));