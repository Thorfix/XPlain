document.addEventListener('DOMContentLoaded', init);

const updateData = async () => {
    await updateCacheMetrics();
    await updateCacheAlerts();
    await updateCacheHealth();
    await updateOptimizationMetrics();
};

const init = async () => {
    await updateCacheMetrics();
    await updateCacheAlerts();
    await updateCacheHealth();
    await updateOptimizationMetrics();
    setInterval(updateData, 5000);
};

const updateCacheMetrics = async () => {
    try {
        const response = await fetch('/api/cache/metrics');
        const metrics = await response.json();
        document.getElementById('cache-metrics').innerHTML = `
            <h3>Cache Metrics</h3>
            <div>Hit Rate: ${(metrics.hitRate * 100).toFixed(1)}%</div>
            <div>Memory Usage: ${metrics.memoryUsage.toFixed(2)} MB</div>
            <div>Item Count: ${metrics.itemCount}</div>
        `;
    } catch (error) {
        console.error('Error updating cache metrics:', error);
    }
};

const updateCacheAlerts = async () => {
    try {
        const response = await fetch('/api/cache/alerts');
        const alerts = await response.json();
        document.getElementById('cache-alerts').innerHTML = `
            <h3>Active Alerts</h3>
            ${alerts.map(alert => `
                <div class="alert ${alert.severity.toLowerCase()}">
                    ${alert.message}
                </div>
            `).join('')}
        `;
    } catch (error) {
        console.error('Error updating cache alerts:', error);
    }
};

const updateCacheHealth = async () => {
    try {
        const response = await fetch('/api/cache/health');
        const health = await response.json();
        document.getElementById('cache-health').innerHTML = `
            <h3>Cache Health</h3>
            <div class="health-status ${health.status.toLowerCase()}">
                ${health.status}
            </div>
        `;
    } catch (error) {
        console.error('Error updating cache health:', error);
    }
};

const updateOptimizationMetrics = async () => {
    try {
        const response = await fetch('/api/cache/optimization/metrics');
        const metrics = await response.json();
        
        // Update optimization metrics section
        const metricsDiv = document.getElementById('optimization-metrics');
        
        const activeOptimizations = metrics.activeOptimizations.map(opt => `
            <div class="optimization-action">
                <strong>${opt.actionType}</strong>
                <span>Started: ${new Date(opt.timestamp).toLocaleString()}</span>
            </div>
        `).join('');

        const successRates = Object.entries(metrics.successRateByStrategy).map(([strategy, rate]) => `
            <div class="strategy-success">
                <strong>${strategy}:</strong> ${(rate * 100).toFixed(1)}% success rate
            </div>
        `).join('');

        const recentActions = metrics.recentActions.slice(0, 5).map(action => `
            <div class="recent-action ${action.wasSuccessful ? 'success' : 'failure'}">
                <strong>${action.actionType}</strong>
                <span>${new Date(action.timestamp).toLocaleString()}</span>
                ${action.rollbackReason ? `<span class="rollback">Rollback: ${action.rollbackReason}</span>` : ''}
            </div>
        `).join('');

        metricsDiv.innerHTML = `
            <h3>Optimization Status</h3>
            <div class="emergency-override ${metrics.emergencyOverrideActive ? 'active' : ''}">
                Emergency Override: ${metrics.emergencyOverrideActive ? 'Active' : 'Inactive'}
                <button onclick="toggleEmergencyOverride(${!metrics.emergencyOverrideActive})">
                    ${metrics.emergencyOverrideActive ? 'Deactivate' : 'Activate'} Override
                </button>
            </div>
            <div class="active-optimizations">
                <h4>Active Optimizations</h4>
                ${activeOptimizations || '<p>No active optimizations</p>'}
            </div>
            <div class="success-rates">
                <h4>Strategy Success Rates</h4>
                ${successRates || '<p>No optimization history</p>'}
            </div>
            <div class="recent-actions">
                <h4>Recent Actions</h4>
                ${recentActions || '<p>No recent actions</p>'}
            </div>
        `;
    } catch (error) {
        console.error('Error updating optimization metrics:', error);
    }
};

const toggleEmergencyOverride = async (enabled) => {
    try {
        await fetch('/api/cache/optimization/emergency-override', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
            },
            body: JSON.stringify(enabled)
        });
        await updateOptimizationMetrics();
    } catch (error) {
        console.error('Error toggling emergency override:', error);
    }
};