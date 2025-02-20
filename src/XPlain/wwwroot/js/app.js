const { useState, useEffect } = React;

function Dashboard() {
    const [health, setHealth] = useState(null);
    const [metrics, setMetrics] = useState(null);
    const [alerts, setAlerts] = useState([]);
    const [analytics, setAnalytics] = useState([]);
    const [recommendations, setRecommendations] = useState([]);

    useEffect(() => {
        const fetchData = async () => {
            try {
                const [healthRes, metricsRes, alertsRes, analyticsRes, recsRes] = await Promise.all([
                    fetch('/api/cache/health'),
                    fetch('/api/cache/metrics'),
                    fetch('/api/cache/alerts'),
                    fetch('/api/cache/analytics/7'),
                    fetch('/api/cache/recommendations')
                ]);

                setHealth(await healthRes.json());
                setMetrics(await metricsRes.json());
                setAlerts(await alertsRes.json());
                setAnalytics(await analyticsRes.json());
                setRecommendations(await recsRes.json());
            } catch (error) {
                console.error('Error fetching data:', error);
            }
        };

        fetchData();
        const interval = setInterval(fetchData, 60000); // Refresh every minute
        return () => clearInterval(interval);
    }, []);

    return (
        <div className="container mx-auto px-4 py-8">
            <h1 className="text-3xl font-bold mb-8">Cache Monitoring Dashboard</h1>
            
            {/* Health Status */}
            {health && (
                <div className="bg-white rounded-lg shadow-lg p-6 mb-8">
                    <h2 className="text-xl font-semibold mb-4">Health Status</h2>
                    <div className="grid grid-cols-4 gap-4">
                        <div className="p-4 bg-gray-50 rounded">
                            <div className="text-sm text-gray-600">Status</div>
                            <div className={`text-lg font-bold ${health.isHealthy ? 'text-green-600' : 'text-red-600'}`}>
                                {health.isHealthy ? 'Healthy' : 'Issues Detected'}
                            </div>
                        </div>
                        <div className="p-4 bg-gray-50 rounded">
                            <div className="text-sm text-gray-600">Hit Ratio</div>
                            <div className="text-lg font-bold">{(health.hitRatio * 100).toFixed(1)}%</div>
                        </div>
                        <div className="p-4 bg-gray-50 rounded">
                            <div className="text-sm text-gray-600">Memory Usage</div>
                            <div className="text-lg font-bold">{health.memoryUsageMB.toFixed(1)} MB</div>
                        </div>
                        <div className="p-4 bg-gray-50 rounded">
                            <div className="text-sm text-gray-600">Response Time</div>
                            <div className="text-lg font-bold">{health.averageResponseTimeMs.toFixed(1)} ms</div>
                        </div>
                    </div>
                </div>
            )}

            {/* Active Alerts */}
            <div className="bg-white rounded-lg shadow-lg p-6 mb-8">
                <h2 className="text-xl font-semibold mb-4">Active Alerts</h2>
                {alerts.length === 0 ? (
                    <p className="text-gray-600">No active alerts</p>
                ) : (
                    <div className="space-y-4">
                        {alerts.map(alert => (
                            <div key={alert.id} className={`p-4 rounded ${
                                alert.severity === 'Error' ? 'bg-red-100' :
                                alert.severity === 'Warning' ? 'bg-yellow-100' :
                                'bg-blue-100'
                            }`}>
                                <div className="font-semibold">{alert.type}</div>
                                <div className="text-sm">{alert.message}</div>
                                <div className="text-xs text-gray-600 mt-1">
                                    {new Date(alert.timestamp).toLocaleString()}
                                </div>
                            </div>
                        ))}
                    </div>
                )}
            </div>

            {/* Performance Metrics */}
            {metrics && (
                <div className="bg-white rounded-lg shadow-lg p-6 mb-8">
                    <h2 className="text-xl font-semibold mb-4">Performance Metrics</h2>
                    <div className="grid grid-cols-3 gap-4">
                        {Object.entries(metrics).map(([key, value]) => (
                            <div key={key} className="p-4 bg-gray-50 rounded">
                                <div className="text-sm text-gray-600">{key}</div>
                                <div className="text-lg font-bold">
                                    {typeof value === 'number' ? value.toFixed(2) : value}
                                </div>
                            </div>
                        ))}
                    </div>
                </div>
            )}

            {/* Recommendations */}
            <div className="bg-white rounded-lg shadow-lg p-6">
                <h2 className="text-xl font-semibold mb-4">Optimization Recommendations</h2>
                {recommendations.length === 0 ? (
                    <p className="text-gray-600">No recommendations at this time</p>
                ) : (
                    <ul className="list-disc pl-5 space-y-2">
                        {recommendations.map((rec, index) => (
                            <li key={index} className="text-gray-700">{rec}</li>
                        ))}
                    </ul>
                )}
            </div>
        </div>
    );
}

ReactDOM.render(<Dashboard />, document.getElementById('root'));