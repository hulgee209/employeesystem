document.addEventListener('DOMContentLoaded', function () {
    const button =
        document.getElementById('aiWidgetButton');

    const panel =
        document.getElementById('aiWidgetPanel');

    const intro =
        document.getElementById('aiWidgetIntro');

    const introClose =
        document.getElementById('aiWidgetIntroClose');

    const minimize =
        document.getElementById('aiWidgetMinimize');

    const form =
        document.getElementById('aiWidgetForm');

    const input =
        document.getElementById('aiWidgetInput');

    const send =
        document.getElementById('aiWidgetSend');

    const messages =
        document.getElementById('aiWidgetMessages');

    if (!button || !panel || !form || !input || !messages) {
        return;
    }

    button.addEventListener('click', function () {
        // Toggle the panel open/close
        const isOpen = panel.classList.contains('open');
        if (isOpen) {
            panel.classList.remove('open');
            // Show intro again when widget closes
            if (intro) {
                intro.style.visibility = 'visible';
            }
        } else {
            panel.classList.add('open');
            // Hide intro when widget opens (but keep clickable)
            if (intro) {
                intro.style.visibility = 'hidden';
            }
            input.focus();
            scrollWidgetToBottom();
            loadChatHistory(); // Load history when opening
        }
    });

    minimize.addEventListener('click', function () {
        panel.classList.remove('open');
        // Show intro again when widget closes
        if (intro) {
            intro.style.visibility = 'visible';
        }
    });

    // Close widget when clicking outside
    document.addEventListener('click', function (event) {
        // Check if click is outside the panel and button
        if (!panel.contains(event.target) && !button.contains(event.target)) {
            panel.classList.remove('open');
        }
    });

    introClose.addEventListener('click', function () {
        intro.style.visibility = 'hidden';
    });

    // Close widget when clicking on intro message - always clickable
    if (intro) {
        intro.style.cursor = 'pointer';
        intro.addEventListener('click', function (event) {
            if (event.target === introClose) return; // Don't process if clicking X button
            panel.classList.add('open');
            intro.style.visibility = 'hidden';
            input.focus();
            scrollWidgetToBottom();
            loadChatHistory();
        });
    }

    form.addEventListener('submit', async function (event) {
        event.preventDefault();

        const question =
            input.value.trim();

        if (!question) {
            return;
        }

        appendWidgetMessage('user', question);
        input.value = '';
        resizeWidgetInput();
        setWidgetLoading(true);

        const typing =
            appendWidgetTyping();

        try {
            const response =
                await fetch('/AIChat/Ask', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json'
                    },
                    body: JSON.stringify({
                        question: question
                    })
                });

            const data =
                await response.json();

            typing.remove();
            appendWidgetMessage(
                'ai',
                data.answer || 'Алдаа гарлаа. Дахин оролдоно уу.');
        }
        catch {
            typing.remove();
            appendWidgetMessage(
                'ai',
                'Сервертэй холбогдож чадсангүй. ASP.NET болон AI backend ажиллаж байгаа эсэхийг шалгана уу.');
        }
        finally {
            setWidgetLoading(false);
            input.focus();
        }
    });

    input.addEventListener('keydown', function (event) {
        if (event.key === 'Enter' && !event.shiftKey) {
            event.preventDefault();
            form.requestSubmit();
        }
    });

    input.addEventListener('input', resizeWidgetInput);

    function hideIntro() {
        if (intro) {
            intro.style.display = 'none';
        }
    }

    function appendWidgetMessage(sender, text) {
        const row =
            document.createElement('div');

        row.className =
            'ai-widget-message ' + sender;

        if (sender === 'ai') {
            const avatar =
                document.createElement('img');

            avatar.className =
                'ai-widget-message-avatar';

            avatar.src =
                '/images/ai-hr-bot.svg';

            avatar.alt =
                '';

            avatar.setAttribute('aria-hidden', 'true');

            row.appendChild(avatar);
        }

        const wrapper =
            document.createElement('div');

        const bubble =
            document.createElement('div');

        bubble.className =
            'ai-widget-bubble';

        bubble.textContent =
            text;

        const time =
            document.createElement('small');

        time.textContent =
            new Date().toLocaleTimeString('mn-MN', {
                hour: '2-digit',
                minute: '2-digit'
            });

        wrapper.appendChild(bubble);
        wrapper.appendChild(time);
        row.appendChild(wrapper);
        messages.appendChild(row);
        scrollWidgetToBottom();

        return row;
    }

    function appendWidgetTyping() {
        const row =
            appendWidgetMessage('ai', 'AI бодож байна...');

        const bubble =
            row.querySelector('.ai-widget-bubble');

        bubble.innerHTML =
            'AI бодож байна ' +
            '<span class="ai-widget-typing">' +
            '<span></span><span></span><span></span>' +
            '</span>';

        return row;
    }

    function setWidgetLoading(isLoading) {
        send.disabled =
            isLoading;

        input.disabled =
            isLoading;
    }

    function resizeWidgetInput() {
        input.style.height =
            'auto';

        input.style.height =
            Math.min(input.scrollHeight, 120) + 'px';
    }

    function scrollWidgetToBottom() {
        messages.scrollTop =
            messages.scrollHeight;
    }

    async function loadChatHistory() {
        const historyList = document.getElementById('aiWidgetHistoryList');
        if (!historyList) return;

        try {
            const response = await fetch('/AIChat/Sessions');
            const sessions = await response.json();

            // Clear existing items
            historyList.innerHTML = '';

            if (!sessions || sessions.length === 0) {
                historyList.innerHTML = '<div style="padding: 12px; text-align: center; color: #94a3b8; font-size: 12px;">Түүх алга</div>';
                return;
            }

            // Сүүлийн 8 харилцан яриаг харуулна.
            sessions.slice(0, 8).forEach(session => {
                const item = document.createElement('button');
                item.type = 'button';
                item.className = `ai-widget-history-item${session.isPinned ? ' pinned' : ''}`;
                item.textContent = session.title || `Session ${session.sessionId.substring(0, 8)}`;
                item.title = session.title || session.sessionId;
                item.dataset.sessionId = session.sessionId;
                item.addEventListener('click', () => loadSessionMessages(session.sessionId, item));
                // Add right-click context menu
                item.addEventListener('contextmenu', (e) => showHistoryContextMenu(e, session.sessionId));
                historyList.appendChild(item);
            });
        } catch (error) {
            console.error('Failed to load chat history:', error);
            historyList.innerHTML = '<div style="padding: 12px; text-align: center; color: #ef4444; font-size: 11px;">Түүх ачаалж чадсангүй</div>';
        }
    }

    async function loadSessionMessages(sessionId, activeItem) {
        // Mark item as active
        document.querySelectorAll('.ai-widget-history-item').forEach(item => item.classList.remove('active'));
        if (activeItem) activeItem.classList.add('active');

        try {
            const response = await fetch(`/AIChat/Session/${sessionId}`);
            if (!response.ok) return;

            const data = await response.json();
            messages.innerHTML = `<div class="ai-widget-date">${new Date(data.createdAt).toLocaleDateString('mn-MN')}</div>`;

            // Load messages from session
            if (data.messages && data.messages.length > 0) {
                data.messages.forEach(msg => {
                    appendWidgetMessage(normalizeMessageSender(msg.role), msg.content);
                });
            }
            scrollWidgetToBottom();
        } catch (error) {
            console.error('Failed to load session:', error);
            appendWidgetMessage('ai', 'Сессийг ачаалж чадсангүй.');
        }
    }

    function showHistoryContextMenu(event, sessionId) {
        event.preventDefault();
        
        // Remove any existing context menu
        const existingMenu = document.querySelector('.ai-widget-context-menu');
        if (existingMenu) existingMenu.remove();
        
        // Create context menu
        const menu = document.createElement('div');
        menu.className = 'ai-widget-context-menu';
        menu.style.position = 'fixed';
        menu.style.left = event.pageX + 'px';
        menu.style.top = event.pageY + 'px';
        
        const deleteBtn = document.createElement('button');
        deleteBtn.className = 'ai-widget-context-item';
        deleteBtn.textContent = 'Устгах';
        deleteBtn.onclick = () => { deleteSession(sessionId); menu.remove(); };
        
        const pinBtn = document.createElement('button');
        pinBtn.className = 'ai-widget-context-item';
        pinBtn.textContent = 'Тогтоох';
        pinBtn.onclick = () => { pinSession(sessionId); menu.remove(); };
        
        menu.appendChild(deleteBtn);
        menu.appendChild(pinBtn);
        document.body.appendChild(menu);
        
        // Close menu when clicking elsewhere
        document.addEventListener('click', () => {
            if (menu.parentNode) menu.remove();
        }, { once: true });
    }

    async function deleteSession(sessionId) {
        const item = document.querySelector(`[data-session-id="${sessionId}"]`);
        if (!confirm('Энэ чатыг устгах уу?')) return;

        try {
            const response = await fetch(`/AIChat/DeleteSession/${sessionId}`, {
                method: 'DELETE'
            });

            if (!response.ok) throw new Error('Delete failed');

            if (item) {
                item.remove();
            }
            messages.innerHTML = '';
        } catch (error) {
            console.error('Failed to delete chat session:', error);
        }
    }

    async function pinSession(sessionId) {
        const item = document.querySelector(`[data-session-id="${sessionId}"]`);
        try {
            const response = await fetch(`/AIChat/PinSession/${sessionId}`, {
                method: 'POST'
            });

            if (!response.ok) throw new Error('Pin failed');

            const data = await response.json();
            if (item) {
                item.classList.toggle('pinned', data.isPinned);
            }
            loadChatHistory();
        } catch (error) {
            console.error('Failed to pin chat session:', error);
        }
    }

    function normalizeMessageSender(role) {
        return String(role).toLowerCase() === 'assistant' ? 'ai' : 'user';
    }
});
