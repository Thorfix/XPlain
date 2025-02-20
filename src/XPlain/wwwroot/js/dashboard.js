// Initialize SignalR connection
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/monitoringHub")
    .configureLogging(signalR.LogLevel.Information)
    .build();

// Initialize charts and metrics displays
let modelPerformanceChart;
let modelHistoryChart;

async function initializeCharts() {
    // Model Performance Chart
    const modelCtx = document.getElementById('modelPerformanceChart').getContext('2d');
    modelPerformanceChart = new Chart(modelCtx, {
        type: 'line',
        data: {
            labels: [],
            datasets: [{
                label: 'Accuracy',
                data: [],
                borderColor: 'rgb(75, 192, 192)',
                tension: 0.1
            }, {
                label: 'F1 Score',
                data: [],
                borderColor: 'rgb(153, 102, 255)',
                tension: 0.1
            }]
        },
        options: {
            responsive: true,
            scales: {
                y: {
                    beginAtZero: true,
                    max: 1
                }
            }
        }
    });

    // Model History Chart
    const historyCtx = document.getElementById('modelHistoryChart').getContext('2d');
    modelHistoryChart = new Chart(historyCtx, {
        type: 'line',
        data: {
            labels: [],
            datasets: [{
                label: 'Performance Over Time',
                data: [],
                borderColor: 'rgb(255, 99, 132)',
                tension: 0.1
            }]
        },
        options: {
            responsive: true,
            scales: {
                y: {
                    beginAtZero: true,
                    max: 1
                }
            }
        }
    });
}

// Update dashboard with new metrics
connection.on("UpdateDashboard", (data) => {
    const metrics = JSON.parse(data);
    
    // Update model performance chart
    updateModelPerformanceChart(metrics.ModelPerformance);
    
    // Update metrics display
    updateMetricsDisplay(metrics);
    
    // Update alerts if any
    if (metrics.ActiveAlerts && metrics.ActiveAlerts.length > 0) {
        showAlerts(metrics.ActiveAlerts);
    }
});

// Handle model alerts
connection.on("ModelAlert", (alert) => {
    const alertsContainer = document.getElementById('alertsContainer');
    const alertElement = document.createElement('div');
    alertElement.className = `alert alert-${alert.Severity.toLowerCase()}`;
    alertElement.innerHTML = `
        <strong>${alert.Title}</strong><br>
        ${alert.Message}<br>
        <small>${new Date(alert.Timestamp).toLocaleString()}</small>
    `;
    alertsContainer.insertBefore(alertElement, alertsContainer.firstChild);
});

// Update model performance chart
function updateModelPerformanceChart(metrics) {
    const timestamp = new Date().toLocaleTimeString();
    
    modelPerformanceChart.data.labels.push(timestamp);
    modelPerformanceChart.data.datasets[0].data.push(metrics.Accuracy);
    modelPerformanceChart.data.datasets[1].data.push(metrics.F1Score);
    
    // Keep only last 20 data points
    if (modelPerformanceChart.data.labels.length > 20) {
        modelPerformanceChart.data.labels.shift();
        modelPerformanceChart.data.datasets[0].data.shift();
        modelPerformanceChart.data.datasets[1].data.shift();
    }
    
    modelPerformanceChart.update();
}

// Update metrics display
function updateMetricsDisplay(metrics) {
    document.getElementById('accuracyMetric').textContent = 
        `${(metrics.ModelPerformance.Accuracy * 100).toFixed(1)}%`;
    document.getElementById('f1ScoreMetric').textContent = 
        `${(metrics.ModelPerformance.F1Score * 100).toFixed(1)}%`;
    document.getElementById('latencyMetric').textContent = 
        `${metrics.ModelPerformance.LatencyMs.toFixed(1)}ms`;
    document.getElementById('lastUpdate').textContent = 
        new Date(metrics.LastUpdate).toLocaleString();
}

// Show alerts
function showAlerts(alerts) {
    const alertsContainer = document.getElementById('alertsContainer');
    alertsContainer.innerHTML = '';
    
    alerts.forEach(alert => {
        const alertElement = document.createElement('div');
        alertElement.className = `alert alert-${alert.Severity.toLowerCase()}`;
        alertElement.innerHTML = `
            <strong>${alert.Title}</strong><br>
            ${alert.Message}<br>
            <small>${new Date(alert.Timestamp).toLocaleString()}</small>
        `;
        alertsContainer.appendChild(alertElement);
    });
}

// Load historical performance data
async function loadHistoricalData() {
    try {
        const response = await fetch('/api/model/performance/history');
        const data = await response.json();
        
        modelHistoryChart.data.labels = data.map(d => new Date(d.Timestamp).toLocaleDateString());
        modelHistoryChart.data.datasets[0].data = data.map(d => d.Accuracy);
        modelHistoryChart.update();
    } catch (error) {
        console.error('Error loading historical data:', error);
    }
}

// Start SignalR connection
async function startConnection() {
    try {
        await connection.start();
        console.log("SignalR Connected.");
        initializeCharts();
        loadHistoricalData();
    } catch (err) {
        console.error(err);
        setTimeout(startConnection, 5000);
    }
}

// Initialize connection
startConnection();