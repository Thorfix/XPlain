// Stress Test Visualization
class StressTestVisualizer {
    constructor() {
        this.charts = {};
        this.initializeCharts();
        this.initializeControls();
    }

    initializeCharts() {
        // System Boundaries Chart
        this.charts.boundaries = new Chart(
            document.getElementById('boundariesChart').getContext('2d'),
            {
                type: 'radar',
                data: {
                    labels: [
                        'Concurrent Users',
                        'Cache Capacity',
                        'ML Predictions/s',
                        'Mitigation Load'
                    ],
                    datasets: [{
                        label: 'System Boundaries',
                        data: [0, 0, 0, 0],
                        backgroundColor: 'rgba(54, 162, 235, 0.2)',
                        borderColor: 'rgb(54, 162, 235)',
                        pointBackgroundColor: 'rgb(54, 162, 235)'
                    }]
                },
                options: {
                    responsive: true,
                    scales: {
                        r: {
                            beginAtZero: true
                        }
                    }
                }
            }
        );

        // Performance Metrics Chart
        this.charts.performance = new Chart(
            document.getElementById('performanceChart').getContext('2d'),
            {
                type: 'line',
                data: {
                    labels: [],
                    datasets: [
                        {
                            label: 'Response Time (ms)',
                            data: [],
                            borderColor: 'rgb(75, 192, 192)'
                        },
                        {
                            label: 'Error Rate (%)',
                            data: [],
                            borderColor: 'rgb(255, 99, 132)'
                        },
                        {
                            label: 'Cache Hit Rate (%)',
                            data: [],
                            borderColor: 'rgb(153, 102, 255)'
                        }
                    ]
                },
                options: {
                    responsive: true,
                    scales: {
                        y: {
                            beginAtZero: true
                        }
                    }
                }
            }
        );
    }

    initializeControls() {
        document.getElementById('startStressTest').addEventListener('click', () => {
            this.runStressTest();
        });
    }

    async runStressTest() {
        try {
            const config = {
                initialUsers: parseInt(document.getElementById('initialUsers').value) || 10,
                loadIncreaseFactor: parseFloat(document.getElementById('loadIncreaseFactor').value) || 1.5,
                stabilityThreshold: parseFloat(document.getElementById('stabilityThreshold').value) || 0.2
            };

            const response = await fetch('/api/loadtest/stress', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(config)
            });

            if (!response.ok) {
                throw new Error('Failed to start stress test');
            }

            const report = await response.json();
            this.visualizeReport(report);
            this.showRecommendations(report.recommendations);
        } catch (error) {
            console.error('Stress test failed:', error);
            this.showError(error.message);
        }
    }

    visualizeReport(report) {
        // Update boundaries chart
        this.charts.boundaries.data.datasets[0].data = [
            report.maxStableUsers,
            report.maxCacheCapacity,
            report.maxMLPredictionRate,
            report.maxMitigationLoad
        ];
        this.charts.boundaries.update();

        // Update performance metrics
        this.updatePerformanceChart(report);

        // Update summary stats
        this.updateSummaryStats(report);
    }

    updatePerformanceChart(report) {
        const labels = [];
        const responseTimes = [];
        const errorRates = [];
        const hitRates = [];

        // Combine all test results chronologically
        const allResults = [
            ...report.concurrencyResults,
            ...report.cacheResults,
            ...report.mlResults,
            ...report.mitigationResults
        ].sort((a, b) => a.timestamp - b.timestamp);

        allResults.forEach((result, index) => {
            labels.push(index);
            responseTimes.push(result.averageResponseTime || 0);
            errorRates.push((result.errorRate || 0) * 100);
            hitRates.push((result.hitRate || 0) * 100);
        });

        this.charts.performance.data.labels = labels;
        this.charts.performance.data.datasets[0].data = responseTimes;
        this.charts.performance.data.datasets[1].data = errorRates;
        this.charts.performance.data.datasets[2].data = hitRates;
        this.charts.performance.update();
    }

    updateSummaryStats(report) {
        document.getElementById('maxUsers').textContent = report.maxStableUsers;
        document.getElementById('maxCache').textContent = report.maxCacheCapacity;
        document.getElementById('maxPredictions').textContent = report.maxMLPredictionRate;
        document.getElementById('maxMitigation').textContent = report.maxMitigationLoad;
    }

    showRecommendations(recommendations) {
        const container = document.getElementById('recommendations');
        container.innerHTML = '<h3>Recommendations</h3><ul>' +
            recommendations.map(rec => `<li>${rec}</li>`).join('') +
            '</ul>';
    }

    showError(message) {
        const container = document.getElementById('stressTestErrors');
        container.innerHTML = `<div class="error-message">${message}</div>`;
    }
}

// Initialize stress test visualization
const stressTestVisualizer = new StressTestVisualizer();