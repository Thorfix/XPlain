// Initialize SignalR connection
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/modelMonitoringHub")
    .withAutomaticReconnect()
    .build();

// Store for metrics data
let metricsData = {
    accuracy: [],
    f1Score: [],
    latency: [],
    featureDistribution: {}
};

// Chart configuration
const chartConfig = {
    responsive: true,
    displayModeBar: false
};

// Initialize charts
function initializeCharts() {
    // Accuracy Chart
    Plotly.newPlot('accuracyChart', [{
        y: [],
        type: 'line',
        name: 'Accuracy',
        line: { color: '#2196F3' }
    }], {
        margin: { t: 20, r: 20, b: 40, l: 40 },
        yaxis: { range: [0, 1] }
    }, chartConfig);

    // F1 Score Chart
    Plotly.newPlot('f1Chart', [{
        y: [],
        type: 'line',
        name: 'F1 Score',
        line: { color: '#4CAF50' }
    }], {
        margin: { t: 20, r: 20, b: 40, l: 40 },
        yaxis: { range: [0, 1] }
    }, chartConfig);

    // Latency Chart
    Plotly.newPlot('latencyChart', [{
        y: [],
        type: 'line',
        name: 'Latency',
        line: { color: '#FF9800' }
    }], {
        margin: { t: 20, r: 20, b: 40, l: 40 }
    }, chartConfig);

    // Feature Distribution Chart
    Plotly.newPlot('featureDistChart', [{
        type: 'box',
        name: 'Distribution'
    }], {
        margin: { t: 20, r: 20, b: 40, l: 40 }
    }, chartConfig);
}

// Update charts with new data
function updateCharts(metrics) {
    // Update accuracy chart
    Plotly.update('accuracyChart', {
        y: [metrics.accuracy]
    });

    // Update F1 score chart
    Plotly.update('f1Chart', {
        y: [metrics.f1Score]
    });

    // Update latency chart
    Plotly.update('latencyChart', {
        y: [metrics.latency]
    });

    // Update feature distribution chart if data available
    if (metrics.featureDistribution) {
        Plotly.update('featureDistChart', {
            y: [Object.values(metrics.featureDistribution)]
        });
    }
}

// Display A/B test results
function displayABTestResults(results) {
    const container = document.getElementById('abTestResults');
    container.innerHTML = `
        <div class="ab-test-card">
            <h3>Test ID: ${results.testId}</h3>
            <div class="test-stats">
                <div class="model-stats">
                    <h4>Model A</h4>
                    <p>Accuracy: ${(results.modelAStats.accuracy * 100).toFixed(2)}%</p>
                    <p>Latency: ${results.modelAStats.meanLatency.toFixed(2)}ms</p>
                    <p>Sample Size: ${results.sampleSizeA}</p>
                </div>
                <div class="model-stats">
                    <h4>Model B</h4>
                    <p>Accuracy: ${(results.modelBStats.accuracy * 100).toFixed(2)}%</p>
                    <p>Latency: ${results.modelBStats.meanLatency.toFixed(2)}ms</p>
                    <p>Sample Size: ${results.sampleSizeB}</p>
                </div>
            </div>
            <div class="test-result">
                <p>Winner: ${results.winner}</p>
                <p>P-Value: ${results.significanceTest.pValue.toFixed(4)}</p>
            </div>
        </div>
    `;
}

// Display alerts
function displayAlerts(alerts) {
    const container = document.getElementById('alertsList');
    container.innerHTML = alerts.map(alert => `
        <div class="alert-card ${alert.severity.toLowerCase()}">
            <h4>${alert.title}</h4>
            <p>${alert.description}</p>
            <span class="timestamp">${new Date(alert.timestamp).toLocaleString()}</span>
        </div>
    `).join('');
}

// Display rollback history
function displayRollbackHistory(history) {
    const container = document.getElementById('rollbackList');
    container.innerHTML = history.map(item => `
        <div class="rollback-card">
            <h4>Rollback at ${new Date(item.timestamp).toLocaleString()}</h4>
            <p>From Version: ${item.fromVersion}</p>
            <p>To Version: ${item.toVersion}</p>
            <p>Reason: ${item.reason}</p>
        </div>
    `).join('');
}

// Initialize time range selector
document.getElementById('timeRange').addEventListener('change', async (e) => {
    const range = e.target.value;
    const response = await fetch(`/api/metrics/performance?range=${range}`);
    const data = await response.json();
    metricsData = data;
    updateCharts(data);
});

// SignalR event handlers
connection.on('ModelMetricsUpdate', (metrics) => {
    metricsData = metrics;
    updateCharts(metrics);
});

connection.on('ABTestUpdate', (results) => {
    displayABTestResults(results);
});

connection.on('AlertsUpdate', (alerts) => {
    displayAlerts(alerts);
});

connection.on('RollbackUpdate', (history) => {
    displayRollbackHistory(history);
});

// Start the connection
async function start() {
    try {
        await connection.start();
        console.log('Connected to SignalR hub');
        initializeCharts();
    } catch (err) {
        console.error('Error connecting to SignalR hub:', err);
        setTimeout(start, 5000);
    }
}

start();