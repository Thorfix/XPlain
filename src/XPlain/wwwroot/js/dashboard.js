let currentMetrics = {};
let metricsCharts = {};
let updateInterval;

function initializeDashboard() {
    setupChartContainers();
    fetchMetrics();
    fetchAlerts();
    fetchPredictions();
    fetchMitigationStatus();
    setupThresholdForm();
    startRealTimeUpdates();
}

async function fetchMitigationStatus() {
    try {
        const response = await fetch('/api/cache/mitigation/status');
        const status = await response.json();
        updateMitigationStatus(status);
    } catch (error) {
        console.error('Error fetching mitigation status:', error);
    }
}

function updateMitigationStatus(status) {
    const container = document.getElementById('mitigation-status');
    if (!container) return;

    const recentMitigations = status.RecentMitigations || [];
    const html = `
        <div class="row">
            <div class="col-md-6">
                <h4>Recent Automatic Actions</h4>
                <ul class="list-group">
                    ${recentMitigations.map(mitigation => `
                        <li class="list-group-item">
                            <strong>${mitigation.Operation}</strong>
                            <br>
                            <small class="text-muted">
                                ${new Date(mitigation.Timestamp).toLocaleString()}
                                (${mitigation.Status})
                            </small>
                        </li>
                    `).join('')}
                </ul>
            </div>
            <div class="col-md-6">
                <h4>Current Thresholds</h4>
                <ul class="list-group">
                    <li class="list-group-item">
                        Min Hit Ratio: ${status.Thresholds.MinHitRatio.toFixed(2)}
                    </li>
                    <li class="list-group-item">
                        Max Memory Usage: ${status.Thresholds.MaxMemoryUsageMB} MB
                    </li>
                    <li class="list-group-item">
                        Max Response Time: ${status.Thresholds.MaxResponseTimeMs} ms
                    </li>
                </ul>
            </div>
        </div>
    `;
    container.innerHTML = html;
}

function setupThresholdForm() {
    const form = document.getElementById('threshold-form');
    if (!form) return;

    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        const thresholds = {
            MinHitRatio: parseFloat(document.getElementById('minHitRatio').value),
            MaxMemoryUsageMB: parseFloat(document.getElementById('maxMemoryUsage').value),
            MaxResponseTimeMs: parseFloat(document.getElementById('maxResponseTime').value)
        };

        try {
            await fetch('/api/cache/mitigation/thresholds', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(thresholds)
            });
            fetchMitigationStatus(); // Refresh the display
        } catch (error) {
            console.error('Error updating thresholds:', error);
            alert('Failed to update thresholds');
        }
    });
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
    const metricColor = getMetricColor(metric);
    
    metricsCharts[metric] = new Chart(ctx, {
        type: 'line',
        data: {
            labels: [],
            datasets: [
                {
                    label: `${formatMetricName(metric)} (Actual)`,
                    data: [],
                    borderColor: metricColor,
                    tension: 0.1,
                    fill: false
                },
                {
                    label: `${formatMetricName(metric)} (Predicted)`,
                    data: [],
                    borderColor: adjustColor(metricColor, 0.6),
                    borderDash: [5, 5],
                    tension: 0.1,
                    fill: false
                },
                {
                    label: 'Confidence Interval (Upper)',
                    data: [],
                    borderColor: 'transparent',
                    backgroundColor: adjustColor(metricColor, 0.2),
                    fill: '+1'
                },
                {
                    label: 'Confidence Interval (Lower)',
                    data: [],
                    borderColor: 'transparent',
                    backgroundColor: adjustColor(metricColor, 0.2),
                    fill: false
                }
            ]
        },
        options: {
            responsive: true,
            interaction: {
                intersect: false,
                mode: 'index'
            },
            scales: {
                y: {
                    beginAtZero: true,
                    grid: {
                        drawBorder: false
                    }
                },
                x: {
                    grid: {
                        display: false
                    }
                }
            },
            plugins: {
                tooltip: {
                    callbacks: {
                        label: function(context) {
                            const value = context.raw;
                            const datasetLabel = context.dataset.label;
                            if (datasetLabel.includes('Confidence')) {
                                return null; // Don't show confidence bounds in tooltip
                            }
                            return `${datasetLabel}: ${value.toFixed(2)}`;
                        }
                    }
                },
                legend: {
                    labels: {
                        filter: function(item) {
                            return !item.text.includes('Confidence'); // Hide confidence bounds from legend
                        }
                    }
                }
            }
        }
    });
}

function adjustColor(color, alpha) {
    const rgb = color.match(/\d+/g);
    return `rgba(${rgb[0]}, ${rgb[1]}, ${rgb[2]}, ${alpha})`;
}

function updateMetricsDisplay(metrics) {
    Object.entries(metrics).forEach(([metric, value]) => {
        if (metricsCharts[metric]) {
            const chart = metricsCharts[metric];
            const now = new Date().toLocaleTimeString();
            
            // Add actual value
            chart.data.labels.push(now);
            chart.data.datasets[0].data.push(value);

            // Add prediction line and confidence interval if available
            if (currentPredictions[metric]) {
                const prediction = currentPredictions[metric];
                const confidenceRange = calculateConfidenceRange(prediction);
                
                chart.data.datasets[1].data.push(prediction.value);
                chart.data.datasets[2].data.push(confidenceRange.upper);
                chart.data.datasets[3].data.push(confidenceRange.lower);
            }
            
            // Remove old data points
            if (chart.data.labels.length > 20) {
                chart.data.labels.shift();
                chart.data.datasets.forEach(dataset => dataset.data.shift());
            }
            
            chart.update();
        }
    });
    updateConfidenceIndicators();
}

function calculateConfidenceRange(prediction) {
    const range = prediction.value * (1 - prediction.confidence);
    return {
        upper: prediction.value + range,
        lower: prediction.value - range
    };
}

function updateConfidenceIndicators() {
    const container = document.getElementById('confidence-indicators');
    if (!container) return;

    let html = '<div class="row">';
    Object.entries(currentPredictions).forEach(([metric, prediction]) => {
        const confidenceClass = prediction.confidence >= 0.85 ? 'high' :
                              prediction.confidence >= 0.6 ? 'medium' : 'low';
        
        html += `
            <div class="col-md-4">
                <div class="confidence-indicator ${confidenceClass}">
                    <h5>${formatMetricName(metric)}</h5>
                    <div class="confidence-gauge">
                        <div class="gauge-fill" style="width: ${prediction.confidence * 100}%"></div>
                    </div>
                    <div class="confidence-details">
                        <span class="confidence-value">${(prediction.confidence * 100).toFixed(1)}%</span>
                        <span class="confidence-label">Confidence</span>
                    </div>
                    ${prediction.detectedPattern ? `
                        <div class="pattern-info">
                            <span class="pattern-type">${prediction.detectedPattern.type}</span>
                            <span class="pattern-time">Lead time: ${formatTimeSpan(prediction.detectedPattern.timeToIssue)}</span>
                        </div>
                    ` : ''}
                </div>
            </div>
        `;
    });
    html += '</div>';
    container.innerHTML = html;
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
        fetchMitigationStatus();
    }, 5000);
}

async function initializeThresholdConfig() {
    try {
        const response = await fetch('/api/cache/mitigation/thresholds');
        const thresholds = await response.json();
        
        // Set initial values in the form
        Object.entries(thresholds).forEach(([metric, config]) => {
            document.querySelector(`input[name="${metric}.warning"]`).value = config.warningThreshold;
            document.querySelector(`input[name="${metric}.critical"]`).value = config.criticalThreshold;
            document.querySelector(`input[name="${metric}.confidence"]`).value = config.minConfidence;
        });
    } catch (error) {
        console.error('Error loading threshold configuration:', error);
    }
}

async function saveThresholds() {
    const form = document.getElementById('thresholdConfigForm');
    const thresholds = {};
    
    ['CacheHitRate', 'MemoryUsage', 'AverageResponseTime'].forEach(metric => {
        thresholds[metric] = {
            warningThreshold: parseFloat(form.querySelector(`input[name="${metric}.warning"]`).value),
            criticalThreshold: parseFloat(form.querySelector(`input[name="${metric}.critical"]`).value),
            minConfidence: parseFloat(form.querySelector(`input[name="${metric}.confidence"]`).value)
        };
    });

    try {
        await fetch('/api/cache/mitigation/thresholds', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(thresholds)
        });

        // Close modal
        const modal = bootstrap.Modal.getInstance(document.getElementById('thresholdConfigModal'));
        modal.hide();

        // Refresh dashboard
        fetchMetrics();
        fetchPredictions();
    } catch (error) {
        console.error('Error saving thresholds:', error);
        alert('Failed to save thresholds');
    }
}

// Initialize threshold configuration when modal is shown
document.getElementById('thresholdConfigModal').addEventListener('show.bs.modal', initializeThresholdConfig);

// Save thresholds when save button is clicked
document.getElementById('saveThresholds').addEventListener('click', saveThresholds);

window.addEventListener('load', initializeDashboard);