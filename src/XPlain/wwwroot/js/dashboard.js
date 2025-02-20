// Cache Monitoring Dashboard
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/cacheHub")
    .build();

let healthChart = null;
let metricsChart = null;

async function initializeDashboard() {
    await Promise.all([
        loadHealthStatus(),
        loadMetrics(),
        loadAlerts(),
        loadAnalytics(),
        loadCircuitBreakerStatus(),
        loadEvictionStats(),
        loadEncryptionStatus(),
        loadMaintenanceLogs()
    ]);
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

async function loadCircuitBreakerStatus() {
    const response = await fetch('/api/cache/circuit-breaker');
    const status = await response.json();
    
    document.getElementById('cbState').textContent = status.status;
    document.getElementById('cbFailures').textContent = status.failureCount;
    document.getElementById('cbLastChange').textContent = new Date(status.lastStateChange).toLocaleString();
    document.getElementById('cbNextRetry').textContent = status.nextRetryTime 
        ? new Date(status.nextRetryTime).toLocaleString() 
        : 'N/A';
    
    const eventsContainer = document.getElementById('cbEvents');
    eventsContainer.innerHTML = '';
    status.recentEvents.forEach(event => {
        const eventEl = document.createElement('div');
        eventEl.className = 'p-2 border-l-4 border-blue-500';
        eventEl.innerHTML = `
            <p class="text-sm">
                <span class="font-semibold">${new Date(event.timestamp).toLocaleString()}</span><br>
                ${event.fromState} → ${event.toState}<br>
                <span class="text-gray-600">${event.reason}</span>
            </p>
        `;
        eventsContainer.appendChild(eventEl);
    });
}

async function loadEvictionStats() {
    const response = await fetch('/api/cache/stats/evictions');
    const stats = await response.json();
    
    const ctx = document.getElementById('evictionChart').getContext('2d');
    new Chart(ctx, {
        type: 'line',
        data: {
            labels: stats.timestamps,
            datasets: [{
                label: 'Evictions/min',
                data: stats.evictionRates
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false
        }
    });
    
    const statsContainer = document.getElementById('evictionStats');
    statsContainer.innerHTML = `
        <p>Total Evictions: ${stats.totalEvictions}</p>
        <p>Avg. Eviction Rate: ${stats.averageEvictionRate}/min</p>
        <p>Peak Eviction Rate: ${stats.peakEvictionRate}/min</p>
    `;
}

async function loadEncryptionStatus() {
    const response = await fetch('/api/cache/encryption');
    const status = await response.json();
    
    document.getElementById('encEnabled').textContent = status.isEnabled ? 'Enabled' : 'Disabled';
    document.getElementById('encCurrentKey').textContent = status.currentKeyId;
    document.getElementById('encKeyCount').textContent = status.keysInRotation;
    document.getElementById('encAutoRotation').textContent = status.autoRotationEnabled ? 'Enabled' : 'Disabled';
    
    const scheduleResponse = await fetch('/api/cache/encryption/rotation');
    const schedule = await scheduleResponse.json();
    
    const scheduleContainer = document.getElementById('keySchedule');
    scheduleContainer.innerHTML = '';
    Object.entries(schedule).forEach(([keyId, rotationTime]) => {
        const scheduleEl = document.createElement('div');
        scheduleEl.className = 'flex justify-between items-center';
        scheduleEl.innerHTML = `
            <span class="font-mono">${keyId}</span>
            <span>${new Date(rotationTime).toLocaleString()}</span>
        `;
        scheduleContainer.appendChild(scheduleEl);
    });
}

async function loadMaintenanceLogs() {
    const response = await fetch('/api/cache/maintenance/logs');
    const logs = await response.json();
    
    const logsContainer = document.getElementById('maintenanceLogs');
    logsContainer.innerHTML = '';
    
    logs.forEach(log => {
        const row = document.createElement('tr');
        row.className = log.status.toLowerCase() === 'error' ? 'bg-red-50' : '';
        row.innerHTML = `
            <td class="px-4 py-2">${new Date(log.timestamp).toLocaleString()}</td>
            <td class="px-4 py-2">${log.operation}</td>
            <td class="px-4 py-2">
                <span class="px-2 py-1 rounded ${getStatusClass(log.status)}">
                    ${log.status}
                </span>
            </td>
            <td class="px-4 py-2">${log.duration}ms</td>
            <td class="px-4 py-2">
                <button class="text-blue-600 hover:text-blue-800" 
                        onclick='showLogDetails(${JSON.stringify(log.metadata)})'>
                    Details
                </button>
            </td>
        `;
        logsContainer.appendChild(row);
    });
}

function getStatusClass(status) {
    switch (status.toLowerCase()) {
        case 'success': return 'bg-green-100 text-green-800';
        case 'warning': return 'bg-yellow-100 text-yellow-800';
        case 'error': return 'bg-red-100 text-red-800';
        default: return 'bg-gray-100 text-gray-800';
    }
}

function showLogDetails(metadata) {
    // Implementation of log details modal
    alert(JSON.stringify(metadata, null, 2));
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
    
    // Refresh circuit breaker status every 10 seconds
    setInterval(loadCircuitBreakerStatus, 10000);
    
    // Refresh eviction stats every minute
    setInterval(loadEvictionStats, 60000);
    
    // Refresh encryption status every minute
    setInterval(loadEncryptionStatus, 60000);
    
    // Refresh maintenance logs every 30 seconds
    setInterval(loadMaintenanceLogs, 30000);
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