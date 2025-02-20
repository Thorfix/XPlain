let currentMetrics = {};
let metricsCharts = {};
let updateInterval;

function initializeDashboard() {
    setupChartContainers();
    fetchMetrics();
    fetchAlerts();
    fetchPredictions();
    startRealTimeUpdates();
}

async function fetchMetrics() {
    try {
        const response = await fetch('/api/cache/metrics');
        const metrics = await response.json();
        currentMetrics = metrics;
        updateMetricsDisplay(metrics);
    } catch (error) {
        console.error('Error fetching metrics:', error);
    }
}

async function fetchPredictions() {
    try {
        const [predictions, predictedAlerts, trends] = await Promise.all([
            fetch('/api/cache/predictions').then(r => r.json()),
            fetch('/api/cache/alerts/predicted').then(r => r.json()),
            fetch('/api/cache/metrics/trends').then(r => r.json())
        ]);

        updatePredictionCharts(predictions);
        updatePredictedAlerts(predictedAlerts);
        updateTrendAnalysis(trends);
    } catch (error) {
        console.error('Error fetching predictions:', error);
    }
}

async function fetchAlerts() {
    try {
        const response = await fetch('/api/cache/alerts');
        const alerts = await response.json();
        updateAlertsDisplay(alerts);
    } catch (error) {
        console.error('Error fetching alerts:', error);
    }
}

function setupChartContainers() {
    const metrics = ['CacheHitRate', 'MemoryUsage', 'AverageResponseTime'];
    const container = document.getElementById('metrics-container');
    
    metrics.forEach(metric => {
        // Current metrics chart
        const chartDiv = document.createElement('div');
        chartDiv.className = 'chart-container';
        chartDiv.innerHTML = `
            <h3>${formatMetricName(metric)}</h3>
            <canvas id="${metric}-chart"></canvas>
        `;
        container.appendChild(chartDiv);

        // Prediction chart
        const predictionDiv = document.createElement('div');
        predictionDiv.className = 'chart-container';
        predictionDiv.innerHTML = `
            <h3>${formatMetricName(metric)} Prediction</h3>
            <canvas id="${metric}-prediction-chart"></canvas>
        `;
        container.appendChild(predictionDiv);

        initializeChart(metric);
    });
}

function initializeChart(metric) {
    const ctx = document.getElementById(`${metric}-chart`).getContext('2d');
    metricsCharts[metric] = new Chart(ctx, {
        type: 'line',
        data: {
            labels: [],
            datasets: [{
                label: formatMetricName(metric),
                data: [],
                borderColor: getMetricColor(metric),
                tension: 0.1
            }]
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

function updateMetricsDisplay(metrics) {
    Object.entries(metrics).forEach(([metric, value]) => {
        if (metricsCharts[metric]) {
            const chart = metricsCharts[metric];
            chart.data.labels.push(new Date().toLocaleTimeString());
            chart.data.datasets[0].data.push(value);
            
            if (chart.data.labels.length > 20) {
                chart.data.labels.shift();
                chart.data.datasets[0].data.shift();
            }
            
            chart.update();
        }
    });
}

function updatePredictionCharts(predictions) {
    Object.entries(predictions).forEach(([metric, prediction]) => {
        const chartElement = document.getElementById(`${metric}-prediction-chart`);
        if (!chartElement) return;

        const data = {
            labels: ['Current', 'Predicted'],
            datasets: [{
                label: metric,
                data: [currentMetrics[metric], prediction.value],
                backgroundColor: ['rgba(75, 192, 192, 0.2)', 'rgba(255, 159, 64, 0.2)'],
                borderColor: ['rgba(75, 192, 192, 1)', 'rgba(255, 159, 64, 1)'],
                borderWidth: 1
            }]
        };

        new Chart(chartElement, {
            type: 'bar',
            data: data,
            options: {
                plugins: {
                    title: {
                        display: true,
                        text: `${metric} Prediction (Confidence: ${(prediction.confidence * 100).toFixed(1)}%)`
                    }
                },
                scales: {
                    y: {
                        beginAtZero: true
                    }
                }
            }
        });
    });
}

function updatePredictedAlerts(alerts) {
    const alertsContainer = document.getElementById('predicted-alerts');
    if (!alertsContainer) return;

    alertsContainer.innerHTML = alerts.map(alert => `
        <div class="alert alert-${getAlertClass(alert.severity)}">
            <strong>${alert.metric}:</strong> Predicted to reach ${alert.predictedValue.toFixed(2)} 
            in ${formatTimeSpan(alert.timeToImpact)}
            (Confidence: ${(alert.confidence * 100).toFixed(1)}%)
        </div>
    `).join('');
}

function updateTrendAnalysis(trends) {
    const trendsContainer = document.getElementById('trend-analysis');
    if (!trendsContainer) return;

    trendsContainer.innerHTML = Object.entries(trends).map(([metric, analysis]) => `
        <div class="trend-card">
            <h4>${metric}</h4>
            <p>Trend: ${analysis.trend}</p>
            <p>Seasonality: ${(analysis.seasonality * 100).toFixed(1)}%</p>
            <p>Volatility: ${analysis.volatility.toFixed(3)}</p>
        </div>
    `).join('');
}

function updateAlertsDisplay(alerts) {
    const alertsContainer = document.getElementById('alerts-container');
    if (!alertsContainer) return;

    alertsContainer.innerHTML = alerts.map(alert => `
        <div class="alert alert-${getAlertClass(alert.severity)}">
            <strong>${alert.type}:</strong> ${alert.message}
        </div>
    `).join('');
}

function getMetricColor(metric) {
    const colors = {
        CacheHitRate: 'rgb(75, 192, 192)',
        MemoryUsage: 'rgb(255, 99, 132)',
        AverageResponseTime: 'rgb(255, 159, 64)'
    };
    return colors[metric] || 'rgb(201, 203, 207)';
}

function getAlertClass(severity) {
    switch (severity.toLowerCase()) {
        case 'critical': return 'danger';
        case 'warning': return 'warning';
        case 'info': return 'info';
        default: return 'secondary';
    }
}

function formatMetricName(metric) {
    return metric.replace(/([A-Z])/g, ' $1').trim();
}

function formatTimeSpan(timeSpan) {
    const minutes = Math.floor(timeSpan / 60000);
    const hours = Math.floor(minutes / 60);
    if (hours > 0) {
        return `${hours}h ${minutes % 60}m`;
    }
    return `${minutes}m`;
}

function startRealTimeUpdates() {
    if (updateInterval) {
        clearInterval(updateInterval);
    }
    updateInterval = setInterval(() => {
        fetchMetrics();
        fetchAlerts();
        fetchPredictions();
    }, 5000);
}

window.addEventListener('load', initializeDashboard);