let currentSessionId = null;
const messagesContainer = document.getElementById('messagesContainer');
const questionInput = document.getElementById('questionInput');
const sendBtn = document.getElementById('sendBtn');
const sessionsList = document.getElementById('sessionsList');
const newChatBtn = document.getElementById('newChatBtn');

document.addEventListener('DOMContentLoaded', async () => {
    loadSessions();
    setupEventListeners();
});

function setupEventListeners() {
    sendBtn?.addEventListener('click', () => askQuestion());
    newChatBtn?.addEventListener('click', startNewSession);

    questionInput?.addEventListener('keypress', (e) => {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            askQuestion();
        }
    });
}

async function askQuestion() {
    const question = questionInput?.value?.trim();
    if (!question) return;

    if (questionInput) questionInput.value = '';
    if (sendBtn) sendBtn.disabled = true;

    addMessageToUI('user', question);

    try {
        const response = await fetch('/AIChat/Ask', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                question: question,
                sessionId: currentSessionId
            })
        });

        if (!response.ok) {
            const error = await response.json();
            addMessageToUI('error', error.answer || 'Алдаа гарлаа.');
            return;
        }

        const data = await response.json();

        if (data.sessionId) {
            currentSessionId = data.sessionId;
        }

        addMessageToUI('assistant', data.answer);
        loadSessions();
    } catch (error) {
        addMessageToUI('error', `Алдаа: ${error.message}`);
    } finally {
        if (sendBtn) sendBtn.disabled = false;
        if (questionInput) questionInput.focus();
    }
}

function addMessageToUI(role, content) {
    if (!messagesContainer) return;

    const msgDiv = document.createElement('div');
    msgDiv.className = `message message-${role}`;

    const timestamp = new Date().toLocaleTimeString('mn-MN', {
        hour: '2-digit',
        minute: '2-digit'
    });
    const roleLabel = role === 'assistant' ? 'AI' : role === 'error' ? 'Алдаа' : 'Та';

    msgDiv.innerHTML = `
        <div class="message-header">
            <strong>${roleLabel}</strong>
            <span class="message-time">${timestamp}</span>
        </div>
        <div class="message-content">${escapeHtml(content)}</div>
    `;

    messagesContainer.appendChild(msgDiv);
    messagesContainer.scrollTop = messagesContainer.scrollHeight;
}

async function loadSessions() {
    if (!sessionsList) return;

    try {
        const response = await fetch('/AIChat/Sessions');
        if (!response.ok) return;

        const sessions = await response.json();
        sessionsList.innerHTML = '';

        sessions.forEach(session => {
            const sessionItem = document.createElement('div');
            sessionItem.className = `list-group-item list-group-item-action chat-session-item ${currentSessionId === session.sessionId ? 'active' : ''} ${session.isPinned ? 'pinned' : ''}`;

            const title = session.title.length > 44 ? session.title.substring(0, 44) + '...' : session.title;
            const date = new Date(session.lastMessageAt).toLocaleDateString('mn-MN');

            sessionItem.innerHTML = `
                <button type="button" class="session-open-btn">
                    <span class="session-title">${escapeHtml(title)}</span>
                    <span class="session-date">${date}</span>
                </button>
                <div class="session-actions">
                    <button type="button" class="btn btn-sm ${session.isPinned ? 'btn-warning' : 'btn-outline-secondary'}" title="${session.isPinned ? 'Тогтоосныг болиулах' : 'Түүхэнд тогтоох'}" onclick="pinSession(event, ${session.sessionId})">${session.isPinned ? '★' : '☆'}</button>
                    <button type="button" class="btn btn-sm btn-outline-danger" title="Устгах" onclick="deleteSession(event, ${session.sessionId})">×</button>
                </div>
            `;

            sessionItem.querySelector('.session-open-btn')?.addEventListener('click', () => loadSession(session.sessionId));
            sessionsList.appendChild(sessionItem);
        });
    } catch (error) {
        console.error('Failed to load sessions:', error);
    }
}

async function loadSession(sessionId) {
    currentSessionId = sessionId;

    if (!messagesContainer) return;
    messagesContainer.innerHTML = '<div class="text-center text-muted">Ачаалж байна...</div>';

    try {
        const response = await fetch(`/AIChat/GetSession/${sessionId}`);
        if (!response.ok) {
            messagesContainer.innerHTML = '<div class="text-danger">Чатын түүхийг ачаалж чадсангүй.</div>';
            return;
        }

        const session = await response.json();

        messagesContainer.innerHTML = '';
        session.messages.forEach(msg => {
            const timestamp = new Date(msg.createdAt).toLocaleTimeString('mn-MN', {
                hour: '2-digit',
                minute: '2-digit'
            });
            const msgDiv = document.createElement('div');
            msgDiv.className = `message message-${msg.role}`;

            const roleLabel = msg.role === 'assistant' ? 'AI' : 'Та';
            msgDiv.innerHTML = `
                <div class="message-header">
                    <strong>${roleLabel}</strong>
                    <span class="message-time">${timestamp}</span>
                </div>
                <div class="message-content">${escapeHtml(msg.content)}</div>
                ${msg.generatedSql ? `<div class="sql-display"><small><strong>SQL:</strong> ${escapeHtml(msg.generatedSql)}</small></div>` : ''}
                ${msg.executionMs > 0 ? `<div class="execution-time"><small>${msg.executionMs}ms</small></div>` : ''}
            `;
            messagesContainer.appendChild(msgDiv);
        });

        messagesContainer.scrollTop = messagesContainer.scrollHeight;
        loadSessions();
    } catch (error) {
        messagesContainer.innerHTML = `<div class="text-danger">Алдаа: ${escapeHtml(error.message)}</div>`;
    }
}

function startNewSession() {
    currentSessionId = null;
    if (messagesContainer) {
        messagesContainer.innerHTML = '<div class="text-center text-muted">Шинэ яриа эхлүүлэхийн тулд асуултаа бичнэ үү.</div>';
    }
    if (questionInput) questionInput.value = '';
    loadSessions();
}

async function deleteSession(event, sessionId) {
    event.stopPropagation();

    if (!confirm('Энэ чатыг устгах уу? Буцаах боломжгүй.')) return;

    try {
        const response = await fetch(`/AIChat/DeleteSession/${sessionId}`, {
            method: 'DELETE'
        });

        if (!response.ok) {
            throw new Error('Delete failed');
        }

        if (currentSessionId === sessionId) {
            startNewSession();
        }
        loadSessions();
    } catch (error) {
        console.error('Failed to delete session:', error);
    }
}

async function pinSession(event, sessionId) {
    event.stopPropagation();

    try {
        const response = await fetch(`/AIChat/PinSession/${sessionId}`, {
            method: 'POST'
        });

        if (!response.ok) {
            throw new Error('Pin failed');
        }

        await loadSessions();
    } catch (error) {
        console.error('Failed to pin session:', error);
    }
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text ?? '';
    return div.innerHTML;
}
