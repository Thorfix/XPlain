// Dashboard initialization and control
document.addEventListener('DOMContentLoaded', () => {
    const startTestBtn = document.getElementById('startTest');
    const stopTestBtn = document.getElementById('stopTest');
    const testProfileSelect = document.getElementById('testProfile');

    // Initialize metrics update interval
    setInterval(updateMetrics, 1000);

    // Load test controls
    startTestBtn.addEventListener('click', async () => {
        const profile = testProfileSelect.value;
        try {
            const response = await fetch('/api/loadtest/start', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({
                    scenarioType: 'CachePerformance',
                    profileName: profile
                })
            });

            if (!response.ok) {
                throw new Error('Failed to start load test');
            }

            startTestBtn.disabled = true;
            stopTestBtn.disabled = false;
            addAlert('info', 'Load test started successfully');
        } catch (error) {
            addAlert('critical', `Failed to start load test: ${error.message}`);
        }
    });

    stopTestBtn.addEventListener('click', async () => {
        try {
            const response = await fetch('/api/loadtest/stop', {
                method: 'POST'
            });

            if (!response.ok) {
                throw new Error('Failed to stop load test');
            }

            startTestBtn.disabled = false;
            stopTestBtn.disabled = true;
            addAlert('info', 'Load test stopped successfully');
        } catch (error) {
            addAlert('critical', `Failed to stop load test: ${error.message}`);
        }
    });
});

// Update dashboard metrics
async function updateMetrics() {
    try {
        const response = await fetch('/api/cache/metrics');
        const metrics = await response.json();

        // Update metric cards
        document.getElementById('cacheHitRate').textContent = `${(metrics.hitRate * 100).toFixed(1)}%`;
        document.getElementById('responseTime').textContent = `${metrics.averageResponseTime.toFixed(1)}ms`;
        document.getElementById('memoryUsage').textContent = `${(metrics.memoryUsageMB).toFixed(1)} MB`;
        document.getElementById('predictionAccuracy').textContent = `${(metrics.predictionAccuracy * 100).toFixed(1)}%`;

        // Update alerts if any threshold is exceeded
        if (metrics.hitRate < 0.7) {
            addAlert('warning', 'Cache hit rate is below 70%');
        }
        if (metrics.averageResponseTime > 1000) {
            addAlert('critical', 'Average response time exceeds 1000ms');
        }
        if (metrics.memoryUsageMB > 1000) {
            addAlert('warning', 'Memory usage exceeds 1GB');
        }
        if (metrics.predictionAccuracy < 0.8) {
            addAlert('warning', 'ML prediction accuracy is below 80%');
        }
    } catch (error) {
        console.error('Failed to update metrics:', error);
    }
}

// Add alert to the dashboard
function addAlert(type, message) {
    const alertsList = document.getElementById('alertsList');
    const alertElement = document.createElement('div');
    alertElement.className = `alert-item ${type}`;
    alertElement.textContent = `${new Date().toLocaleTimeString()}: ${message}`;
    
    alertsList.insertBefore(alertElement, alertsList.firstChild);
    
    // Remove old alerts if there are too many
    while (alertsList.children.length > 10) {
        alertsList.removeChild(alertsList.lastChild);
    }

    // Auto-remove alert after 5 minutes
    setTimeout(() => {
        alertElement.remove();
    }, 300000);
}