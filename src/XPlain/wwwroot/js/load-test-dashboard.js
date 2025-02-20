// Load Test Dashboard Component
class LoadTestDashboard {
    constructor() {
        this.charts = {};
        this.currentTest = null;
        this.initialize();
    }

    initialize() {
        this.initializeCharts();
        this.initializeControls();
        this.setupWebSocket();
    }

    initializeCharts() {
        // Response Time Distribution
        this.charts.responseTime = new Chart(
            document.getElementById('responseTimeChart').getContext('2d'),
            {
                type: 'line',
                data: {
                    labels: [],
                    datasets: [
                        {
                            label: 'Average Response Time',
                            data: [],
                            borderColor: 'rgb(75, 192, 192)'
                        },
                        {
                            label: 'P95 Response Time',
                            data: [],
                            borderColor: 'rgb(255, 99, 132)'
                        }
                    ]
                },
                options: {
                    responsive: true,
                    animation: false,
                    scales: {
                        y: {
                            beginAtZero: true,
                            title: {
                                display: true,
                                text: 'Response Time (ms)'
                            }
                        }
                    }
                }
            }
        );

        // Cache Performance
        this.charts.cache = new Chart(
            document.getElementById('cacheChart').getContext('2d'),
            {
                type: 'line',
                data: {
                    labels: [],
                    datasets: [
                        {
                            label: 'Cache Hit Rate',
                            data: [],
                            borderColor: 'rgb(54, 162, 235)'
                        },
                        {
                            label: 'ML Prediction Accuracy',
                            data: [],
                            borderColor: 'rgb(153, 102, 255)'
                        }
                    ]
                },
                options: {
                    responsive: true,
                    animation: false,
                    scales: {
                        y: {
                            beginAtZero: true,
                            max: 1,
                            title: {
                                display: true,
                                text: 'Rate'
                            }
                        }
                    }
                }
            }
        );

        // Traffic Pattern
        this.charts.traffic = new Chart(
            document.getElementById('trafficChart').getContext('2d'),
            {
                type: 'bar',
                data: {
                    labels: ['Technical', 'Documentation', 'General', 'Automated'],
                    datasets: [{
                        label: 'Query Distribution',
                        data: [0, 0, 0, 0],
                        backgroundColor: [
                            'rgba(75, 192, 192, 0.5)',
                            'rgba(255, 99, 132, 0.5)',
                            'rgba(54, 162, 235, 0.5)',
                            'rgba(153, 102, 255, 0.5)'
                        ]
                    }]
                },
                options: {
                    responsive: true,
                    animation: false,
                    scales: {
                        y: {
                            beginAtZero: true,
                            title: {
                                display: true,
                                text: 'Query Count'
                            }
                        }
                    }
                }
            }
        );

        // System Load
        this.charts.system = new Chart(
            document.getElementById('systemChart').getContext('2d'),
            {
                type: 'line',
                data: {
                    labels: [],
                    datasets: [
                        {
                            label: 'Active Users',
                            data: [],
                            borderColor: 'rgb(255, 159, 64)'
                        },
                        {
                            label: 'Error Rate',
                            data: [],
                            borderColor: 'rgb(255, 99, 132)'
                        }
                    ]
                },
                options: {
                    responsive: true,
                    animation: false,
                    scales: {
                        y: {
                            beginAtZero: true,
                            title: {
                                display: true,
                                text: 'Count'
                            }
                        }
                    }
                }
            }
        );
    }

    initializeControls() {
        const testProfiles = {
            'BusinessHours': '9AM-5PM Normal Load',
            'PeakLoad': '11AM-2PM High Load',
            'AfterHours': '5PM-11PM Moderate Load',
            'NightTime': '11PM-5AM Low Load',
            'MaintenanceWindow': '2AM-4AM Minimal Load'
        };

        const profileSelect = document.getElementById('testProfile');
        Object.entries(testProfiles).forEach(([value, text]) => {
            const option = document.createElement('option');
            option.value = value;
            option.textContent = text;
            profileSelect.appendChild(option);
        });

        document.getElementById('startTest').addEventListener('click', () => this.startLoadTest());
        document.getElementById('stopTest').addEventListener('click', () => this.stopLoadTest());
        document.getElementById('generateReport').addEventListener('click', () => this.generateTestReport());
    }

    setupWebSocket() {
        const connection = new signalR.HubConnectionBuilder()
            .withUrl("/loadTestHub")
            .withAutomaticReconnect()
            .build();

        connection.on("UpdateMetrics", (metrics) => this.updateDashboard(metrics));
        connection.start().catch(err => console.error(err));
    }

    async startLoadTest() {
        const profile = document.getElementById('testProfile').value;
        try {
            const response = await fetch('/api/loadtest/start', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    scenarioType: 'CachePerformance',
                    profileName: profile
                })
            });

            if (!response.ok) throw new Error('Failed to start test');
            
            this.currentTest = {
                startTime: new Date(),
                profile: profile
            };
            
            this.updateControls(true);
            this.showNotification('Load test started successfully', 'success');
        } catch (error) {
            this.showNotification('Failed to start load test: ' + error.message, 'error');
        }
    }

    async stopLoadTest() {
        try {
            const response = await fetch('/api/loadtest/stop', {
                method: 'POST'
            });

            if (!response.ok) throw new Error('Failed to stop test');
            
            this.updateControls(false);
            this.showNotification('Load test stopped successfully', 'success');
        } catch (error) {
            this.showNotification('Failed to stop load test: ' + error.message, 'error');
        }
    }

    async generateTestReport() {
        try {
            const response = await fetch('/api/loadtest/report');
            const report = await response.json();
            
            // Display report in modal
            const modal = document.getElementById('reportModal');
            const reportContent = document.getElementById('reportContent');
            reportContent.innerHTML = this.formatReport(report);
            modal.style.display = 'block';
        } catch (error) {
            this.showNotification('Failed to generate report: ' + error.message, 'error');
        }
    }

    updateDashboard(metrics) {
        const timestamp = new Date().toLocaleTimeString();

        // Update charts
        this.updateChart(this.charts.responseTime, timestamp, [
            metrics.averageResponseTime,
            metrics.customMetrics.p95ResponseTime
        ]);

        this.updateChart(this.charts.cache, timestamp, [
            metrics.customMetrics.cacheHitRate,
            metrics.customMetrics.predictionAccuracy
        ]);

        this.charts.traffic.data.datasets[0].data = [
            metrics.customMetrics.technicalQueries || 0,
            metrics.customMetrics.documentationQueries || 0,
            metrics.customMetrics.generalQueries || 0,
            metrics.customMetrics.automatedQueries || 0
        ];
        this.charts.traffic.update();

        this.updateChart(this.charts.system, timestamp, [
            metrics.activeUsers,
            metrics.errorRate
        ]);

        // Update summary metrics
        this.updateSummaryMetrics(metrics);
    }

    updateChart(chart, label, values) {
        chart.data.labels.push(label);
        values.forEach((value, index) => {
            chart.data.datasets[index].data.push(value);
        });

        // Keep last 50 data points
        if (chart.data.labels.length > 50) {
            chart.data.labels.shift();
            chart.data.datasets.forEach(dataset => dataset.data.shift());
        }

        chart.update();
    }

    updateSummaryMetrics(metrics) {
        document.getElementById('activeUsers').textContent = metrics.activeUsers;
        document.getElementById('avgResponseTime').textContent = `${metrics.averageResponseTime.toFixed(2)}ms`;
        document.getElementById('errorRate').textContent = `${(metrics.errorRate * 100).toFixed(2)}%`;
        document.getElementById('cacheHitRate').textContent = `${(metrics.customMetrics.cacheHitRate * 100).toFixed(2)}%`;
        document.getElementById('predictionAccuracy').textContent = 
            `${(metrics.customMetrics.predictionAccuracy * 100).toFixed(2)}%`;
    }

    updateControls(testRunning) {
        document.getElementById('startTest').disabled = testRunning;
        document.getElementById('stopTest').disabled = !testRunning;
        document.getElementById('testProfile').disabled = testRunning;
        document.getElementById('generateReport').disabled = !this.currentTest;
    }

    showNotification(message, type) {
        const notification = document.createElement('div');
        notification.className = `notification ${type}`;
        notification.textContent = message;
        
        document.getElementById('notifications').appendChild(notification);
        setTimeout(() => notification.remove(), 5000);
    }

    formatReport(report) {
        return `
            <h2>Load Test Report</h2>
            <h3>Test Configuration</h3>
            <ul>
                <li>Profile: ${report.configuration.profile}</li>
                <li>Duration: ${report.configuration.duration} minutes</li>
                <li>Peak Users: ${report.performance.peakUsers}</li>
            </ul>

            <h3>Performance Metrics</h3>
            <ul>
                <li>Average Response Time: ${report.performance.averageResponseTime.toFixed(2)}ms</li>
                <li>P95 Response Time: ${report.performance.p95ResponseTime.toFixed(2)}ms</li>
                <li>Error Rate: ${(report.performance.errorRate * 100).toFixed(2)}%</li>
                <li>Cache Hit Rate: ${(report.performance.cacheHitRate * 100).toFixed(2)}%</li>
            </ul>

            <h3>ML Prediction Performance</h3>
            <ul>
                <li>Overall Accuracy: ${(report.mlPredictions.accuracy * 100).toFixed(2)}%</li>
                <li>False Positives: ${report.mlPredictions.falsePositives}</li>
                <li>False Negatives: ${report.mlPredictions.falseNegatives}</li>
            </ul>

            <h3>Recommendations</h3>
            <ul>
                ${report.recommendations.map(r => `<li>${r}</li>`).join('')}
            </ul>
        `;
    }
}