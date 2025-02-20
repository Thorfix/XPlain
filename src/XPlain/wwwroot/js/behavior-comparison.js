// Cache Behavior Comparison Visualization
class BehaviorComparisonVisualizer {
    constructor(containerId) {
        this.containerId = containerId;
        this.chart = null;
        this.initialize();
    }

    initialize() {
        const ctx = document.getElementById(this.containerId).getContext('2d');
        this.chart = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: [],
                datasets: [
                    {
                        label: 'Predicted Hit Rate',
                        backgroundColor: 'rgba(75, 192, 192, 0.5)',
                        data: []
                    },
                    {
                        label: 'Actual Hit Rate',
                        backgroundColor: 'rgba(255, 99, 132, 0.5)',
                        data: []
                    },
                    {
                        label: 'Predicted Latency (ms)',
                        backgroundColor: 'rgba(54, 162, 235, 0.5)',
                        data: []
                    },
                    {
                        label: 'Actual Latency (ms)',
                        backgroundColor: 'rgba(153, 102, 255, 0.5)',
                        data: []
                    }
                ]
            },
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
                        text: 'Predicted vs Actual Cache Behavior'
                    }
                }
            }
        });
    }

    updateData(report) {
        const patterns = Object.keys(report.patternAnalysis);
        this.chart.data.labels = patterns;
        
        this.chart.data.datasets[0].data = patterns.map(p => 
            report.patternAnalysis[p].predictedHitRate * 100);
        this.chart.data.datasets[1].data = patterns.map(p => 
            report.patternAnalysis[p].actualHitRate * 100);
        this.chart.data.datasets[2].data = patterns.map(p => 
            report.patternAnalysis[p].averagePredictedLatency);
        this.chart.data.datasets[3].data = patterns.map(p => 
            report.patternAnalysis[p].averageActualLatency);
        
        this.chart.update();

        // Update recommendations
        this.updateRecommendations(report.recommendations);
    }

    updateRecommendations(recommendations) {
        const container = document.getElementById('behaviorRecommendations');
        container.innerHTML = '<h3>ML Model Recommendations</h3>';
        const list = document.createElement('ul');
        recommendations.forEach(rec => {
            const item = document.createElement('li');
            item.textContent = rec;
            list.appendChild(item);
        });
        container.appendChild(list);
    }
}

// Initialize and update behavior comparison
const behaviorVisualizer = new BehaviorComparisonVisualizer('behaviorComparisonChart');

// Fetch and update behavior data periodically
setInterval(async () => {
    try {
        const response = await fetch('/api/loadtest/behavior-report');
        const report = await response.json();
        behaviorVisualizer.updateData(report);
    } catch (error) {
        console.error('Failed to fetch behavior report:', error);
    }
}, 5000);