let currentPushData = null;
let repositories = [];
let branchesData = {}; // Store branches data for filtering

// Check worker status on page load
document.addEventListener('DOMContentLoaded', async () => {
    checkWorkerStatus();
    await loadRepositories();

    // Refresh status every 5 seconds
    setInterval(checkWorkerStatus, 5000);
});

async function loadRepositories() {
    const container = document.getElementById('repositories');

    try {
        const response = await fetch('/api/git/repositories');
        repositories = await response.json();

        // Create repository cards dynamically
        container.innerHTML = repositories.map(repo => `
            <div class="repo-card">
                <div class="repo-header">
                    <h2>📂 ${repo.name}</h2>
                    <button class="btn btn-refresh" onclick="loadBranches('${repo.name}')">↻ Refresh</button>
                </div>
                <div class="search-container">
                    <input type="text"
                           class="search-input"
                           id="${repo.name}-search"
                           placeholder="🔍 Search branches..."
                           oninput="filterBranches('${repo.name}')">
                </div>
                <div id="${repo.name}-branches" class="branches-list">
                    <div class="loading">Loading...</div>
                </div>
            </div>
        `).join('');

        // Load branches for each repository
        repositories.forEach(repo => {
            loadBranches(repo.name);
        });
    } catch (error) {
        console.error('Error loading repositories:', error);
        container.innerHTML = '<div class="error">Error loading repositories</div>';
    }
}

async function checkWorkerStatus() {
    try {
        const response = await fetch('/api/git/status');
        const data = await response.json();

        const statusElement = document.getElementById('worker-status');
        if (data.connected) {
            statusElement.textContent = 'Worker Online';
            statusElement.className = 'status-indicator online';
        } else {
            statusElement.textContent = 'Worker Offline';
            statusElement.className = 'status-indicator offline';
        }
    } catch (error) {
        console.error('Error checking worker status:', error);
    }
}

async function loadBranches(repository) {
    const containerId = `${repository}-branches`;
    const container = document.getElementById(containerId);

    container.innerHTML = '<div class="loading">Loading branches...</div>';

    try {
        const response = await fetch(`/api/git/branches/${repository}`);

        if (response.status === 503) {
            container.innerHTML = '<div class="error">Worker is offline. Please start the worker.</div>';
            return;
        }

        if (!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }

        const branches = await response.json();

        if (branches.length === 0) {
            container.innerHTML = '<div class="loading">No branches found</div>';
            return;
        }

        // Store branches data for filtering
        branchesData[repository] = branches;

        // Render branches
        renderBranches(repository, branches);
    } catch (error) {
        console.error(`Error loading ${repository} branches:`, error);
        container.innerHTML = `<div class="error">Error loading branches: ${error.message}</div>`;
    }
}

function renderBranches(repository, branches) {
    const containerId = `${repository}-branches`;
    const container = document.getElementById(containerId);
    const otherRepo = repositories.find(r => r.name !== repository);
    const otherRepoName = otherRepo ? otherRepo.name : 'Other';

    container.innerHTML = branches.map(branch => `
        <div class="branch-item" data-branch-name="${branch.name.toLowerCase()}" onclick="showCommits('${repository}', '${branch.name}')">
            <div class="branch-name">
                🔹 ${branch.name}
                ${branch.isSynchronizedWithOther ? '<span style="color: #4caf50; margin-left: 8px;">✓ Synced</span>' : ''}
            </div>
            <div class="branch-info">📝 ${branch.lastCommitMessage}</div>
            <div class="branch-info">👤 ${branch.lastCommitAuthor}</div>
            <div class="branch-info">🕒 ${formatDate(branch.lastCommitDate)}</div>
            <div class="branch-actions" onclick="event.stopPropagation()">
                <button class="btn btn-push"
                        ${branch.isSynchronizedWithOther ? 'disabled style="opacity: 0.5; cursor: not-allowed;"' : ''}
                        onclick="showPushModal('${branch.name}', '${repository}', '${otherRepoName}')">
                    ${branch.isSynchronizedWithOther ? '✓ Synchronized' : `Push to ${otherRepoName} →`}
                </button>
            </div>
        </div>
    `).join('');
}

function filterBranches(repository) {
    const searchInput = document.getElementById(`${repository}-search`);
    const searchTerm = searchInput.value.toLowerCase();
    const branches = branchesData[repository];

    if (!branches) return;

    if (searchTerm === '') {
        // Show all branches
        renderBranches(repository, branches);
    } else {
        // Filter branches
        const filteredBranches = branches.filter(branch =>
            branch.name.toLowerCase().includes(searchTerm)
        );
        renderBranches(repository, filteredBranches);
    }
}

async function showCommits(repository, branch) {
    const modal = document.getElementById('commits-modal');
    const title = document.getElementById('modal-title');
    const list = document.getElementById('commits-list');

    title.textContent = `Commits: ${repository} / ${branch}`;
    list.innerHTML = '<div class="loading">Loading commits...</div>';
    modal.classList.add('show');

    try {
        const response = await fetch(`/api/git/commits/${repository}/${encodeURIComponent(branch)}?count=20`);

        if (!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }

        const commits = await response.json();

        if (commits.length === 0) {
            list.innerHTML = '<div class="loading">No commits found</div>';
            return;
        }

        list.innerHTML = commits.map(commit => `
            <div class="commit-item">
                <div class="commit-hash">${commit.shortHash} (${commit.hash})</div>
                <div class="commit-message">${commit.message}</div>
                <div class="commit-author">👤 ${commit.author} • 🕒 ${formatDate(commit.date)}</div>
            </div>
        `).join('');
    } catch (error) {
        console.error('Error loading commits:', error);
        list.innerHTML = `<div class="error">Error loading commits: ${error.message}</div>`;
    }
}

function closeCommitsModal() {
    document.getElementById('commits-modal').classList.remove('show');
}

function showPushModal(branch, from, to) {
    currentPushData = { branch, from, to };

    const modal = document.getElementById('push-modal');
    const message = document.getElementById('push-message');

    message.textContent = `Push branch "${branch}" from ${from} to ${to}?`;
    document.getElementById('push-result').innerHTML = '';
    document.getElementById('push-result').className = 'push-result';

    modal.classList.add('show');
}

function closePushModal() {
    document.getElementById('push-modal').classList.remove('show');
    currentPushData = null;
}

async function confirmPush(force) {
    if (!currentPushData) return;

    const resultDiv = document.getElementById('push-result');
    resultDiv.innerHTML = '<div class="loading">Pushing branch...</div>';
    resultDiv.className = 'push-result';

    try {
        const response = await fetch('/api/git/push', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                branch: currentPushData.branch,
                fromRepository: currentPushData.from,
                toRepository: currentPushData.to,
                force: force
            })
        });

        const result = await response.json();

        if (result.success) {
            resultDiv.className = 'push-result success';
            resultDiv.innerHTML = `
                <strong>✅ Success!</strong>
                <p>${result.message}</p>
            `;

            // Refresh branches after successful push
            setTimeout(() => {
                repositories.forEach(repo => loadBranches(repo.name));
            }, 2000);
        } else {
            resultDiv.className = 'push-result error';
            resultDiv.innerHTML = `
                <strong>❌ Failed</strong>
                <p>${result.message}</p>
                ${result.hasConflicts ? '<p><strong>Conflicts detected!</strong> Use Force Push if you\'re sure.</p>' : ''}
            `;
        }
    } catch (error) {
        console.error('Error pushing branch:', error);
        resultDiv.className = 'push-result error';
        resultDiv.innerHTML = `
            <strong>❌ Error</strong>
            <p>${error.message}</p>
        `;
    }
}

function formatDate(dateString) {
    const date = new Date(dateString);
    const now = new Date();
    const diffMs = now - date;
    const diffMins = Math.floor(diffMs / 60000);
    const diffHours = Math.floor(diffMs / 3600000);
    const diffDays = Math.floor(diffMs / 86400000);

    const absoluteTime = date.toLocaleDateString() + ' ' + date.toLocaleTimeString();

    let relativeTime;
    if (diffMins < 1) {
        relativeTime = 'just now';
    } else if (diffMins < 60) {
        relativeTime = `${diffMins} minute${diffMins > 1 ? 's' : ''} ago`;
    } else if (diffHours < 24) {
        relativeTime = `${diffHours} hour${diffHours > 1 ? 's' : ''} ago`;
    } else if (diffDays < 7) {
        relativeTime = `${diffDays} day${diffDays > 1 ? 's' : ''} ago`;
    } else {
        relativeTime = absoluteTime;
    }

    return `<span title="${absoluteTime}">${relativeTime}</span>`;
}

// Close modals when clicking outside
window.onclick = function(event) {
    const commitsModal = document.getElementById('commits-modal');
    const pushModal = document.getElementById('push-modal');

    if (event.target === commitsModal) {
        closeCommitsModal();
    }
    if (event.target === pushModal) {
        closePushModal();
    }
}
