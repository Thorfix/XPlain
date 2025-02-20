// Load Test Metrics Visualization
class LoadTestMetricsVisualizer {
    constructor(containerId) {
        this.containerId = containerId;
        this.chart = null;
        this.metricsData = {
            labels: [],
            datasets: [
                {
                    label: 'Active Users',
                    data: [],
                    borderColor: 'rgb(75, 192, 192)',
                    tension: 0.1
                },
                {
                    label: 'Response Time (ms)',
                    data: [],
                    borderColor: 'rgb(255, 99, 132)',
                    tension: 0.1
                },
                {
                    label: 'Cache Hit Rate',
                    data: [],
                    borderColor: 'rgb(54, 162, 235)',
                    tension: 0.1
                },
                {
                    label: 'ML Prediction Accuracy',
                    data: [],
                    borderColor: 'rgb(153, 102, 255)',
                    tension: 0.1
                }
            ]
        };
    }

    initialize() {
        const ctx = document.getElementById(this.containerId).getContext('2d');
        this.chart = new Chart(ctx, {
            type: 'line',
            data: this.metricsData,
            options: {
                responsive: true,
                scales: {
                    y: {
                        beginAtZero: true
                    }
                },
                plugins: {
                    title: {
                        display: true,
                        text: 'Load Test Real-time Metrics'
                    }
                }
            }
        });
    }

    updateMetrics(metrics) {
        const timestamp = new Date().toLocaleTimeString();
        
        this.metricsData.labels.push(timestamp);
        this.metricsData.datasets[0].data.push(metrics.activeUsers);
        this.metricsData.datasets[1].data.push(metrics.averageResponseTime);
        this.metricsData.datasets[2].data.push(metrics.customMetrics?.cache_hit_rate || 0);
        this.metricsData.datasets[3].data.push(metrics.customMetrics?.prediction_accuracy || 0);

        // Keep last 60 data points
        if (this.metricsData.labels.length > 60) {
            this.metricsData.labels.shift();
            this.metricsData.datasets.forEach(dataset => dataset.data.shift());
        }

        this.chart.update();
    }
}

// Connection management
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/loadTestHub")
    .withAutomaticReconnect()
    .build();

const metricsVisualizer = new LoadTestMetricsVisualizer('loadTestChart');

connection.on("UpdateMetrics", (metrics) => {
    metricsVisualizer.updateMetrics(metrics);
});

connection.start().then(() => {
    metricsVisualizer.initialize();
}).catch(err => console.error(err));