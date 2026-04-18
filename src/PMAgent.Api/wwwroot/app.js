const STORAGE_KEY = "pm-agent-chat-history-v2";
const FORM_STORAGE_KEY = "pm-agent-chat-form-v2";
const ACTIVE_SESSION_STORAGE_KEY = "pm-agent-active-hiring-session-v1";

const elements = {
    workflow: document.getElementById("workflow"),
    iterations: document.getElementById("iterations"),
    context: document.getElementById("context"),
    hiringPanel: document.getElementById("hiringPanel"),
    jobDescription: document.getElementById("jobDescription"),
    jobDescriptionFile: document.getElementById("jobDescriptionFile"),
    candidateCv: document.getElementById("candidateCv"),
    candidateCvFile: document.getElementById("candidateCvFile"),
    technicalInterviewRole: document.getElementById("technicalInterviewRole"),
    targetSeniority: document.getElementById("targetSeniority"),
    autoApproveInterviewSchedule: document.getElementById("autoApproveInterviewSchedule"),
    sessionPanel: document.getElementById("sessionPanel"),
    sessionStage: document.getElementById("sessionStage"),
    sessionSpeaker: document.getElementById("sessionSpeaker"),
    sessionSummary: document.getElementById("sessionSummary"),
    approvalRow: document.getElementById("approvalRow"),
    approveButton: document.getElementById("approveButton"),
    rejectButton: document.getElementById("rejectButton"),
    exportNotesButton: document.getElementById("exportNotesButton"),
    timelinePanel: document.getElementById("timelinePanel"),
    timelineList: document.getElementById("timelineList"),
    messages: document.getElementById("messages"),
    form: document.getElementById("chatForm"),
    prompt: document.getElementById("prompt"),
    sendButton: document.getElementById("sendButton"),
    status: document.getElementById("status"),
    resultTemplate: document.getElementById("resultTemplate"),
    historyItemTemplate: document.getElementById("historyItemTemplate"),
    historyList: document.getElementById("historyList"),
    clearHistoryButton: document.getElementById("clearHistoryButton"),
    copySampleButton: document.getElementById("copySampleButton"),
    sampleRequest: document.getElementById("sampleRequest"),
    timelineItemTemplate: document.getElementById("timelineItemTemplate")
};

const presets = {
    "delivery-release": {
        workflow: "delivery",
        prompt: "Plan the release of a multi-tenant analytics dashboard for enterprise customers.",
        context: "Team of 6, 10-week deadline, SSO and audit logging are mandatory.",
        maxIterationsPerAgent: 10
    },
    "delivery-risk": {
        workflow: "delivery",
        prompt: "Assess the delivery risks for migrating a legacy billing platform to a new SaaS architecture.",
        context: "Need a phased rollout, zero data loss, and weekend cutover planning.",
        maxIterationsPerAgent: 12
    },
    "hiring-backend": {
        workflow: "hiring",
        prompt: "Screen and interview a senior backend engineer for the next release.",
        context: "Remote-first team, two interviewers available this week.",
        jobDescription: "Strong C#, ASP.NET Core, PostgreSQL, API design, cloud deployment, and production support experience.",
        candidateCv: "6 years building .NET APIs, PostgreSQL tuning, Docker, Azure, CI/CD, and incident response leadership.",
        targetSeniority: "SENIOR",
        technicalInterviewRole: "DEV",
        autoApproveInterviewSchedule: true
    },
    "hiring-qa": {
        workflow: "hiring",
        prompt: "Screen and interview a QA engineer for the platform team.",
        context: "Need automation ownership and strong regression strategy.",
        jobDescription: "Need API testing, Playwright, CI pipelines, exploratory testing, and defect triage experience.",
        candidateCv: "Candidate has Playwright, Postman, REST API automation, Azure DevOps pipelines, and regression leadership.",
        targetSeniority: "MID",
        technicalInterviewRole: "TEST",
        autoApproveInterviewSchedule: true
    }
};

let conversationHistory = [];
let activeHiringSession = null;

function syncHiringPanel() {
    elements.hiringPanel.hidden = elements.workflow.value !== "hiring";
    updateComposerState();
    refreshSampleRequest();
    persistFormState();
}

function appendUserMessage(text) {
    const article = document.createElement("article");
    article.className = "message user";
    article.innerHTML = `<div class="bubble"><p>${escapeHtml(text)}</p></div>`;
    elements.messages.appendChild(article);
    scrollMessages();
}

function appendAssistantResult(result) {
    const fragment = elements.resultTemplate.content.cloneNode(true);
    renderMarkdownInto(fragment.querySelector(".result-summary"), result.summary);

    const rolesHost = fragment.querySelector(".result-roles");
    for (const output of result.agentOutputs ?? []) {
        const details = document.createElement("details");
        const summary = document.createElement("summary");
        summary.textContent = `${output.role} · ${output.decision} · confidence ${Number(output.confidence ?? 0).toFixed(2)}`;
        const content = document.createElement("div");
        content.className = "role-content";
        renderMarkdownInto(content, output.output);
        details.append(summary, content);
        rolesHost.appendChild(details);
    }

    elements.messages.appendChild(fragment);
    scrollMessages();
}

function appendSessionUpdate(session) {
    const article = document.createElement("article");
    article.className = "message assistant";
    const bubble = document.createElement("div");
    bubble.className = "bubble result-bubble";
    const content = document.createElement("div");
    renderMarkdownInto(content, buildSessionMarkdown(session));
    bubble.appendChild(content);
    article.appendChild(bubble);
    elements.messages.appendChild(article);
    scrollMessages();
}

function appendError(message) {
    const article = document.createElement("article");
    article.className = "message assistant";
    article.innerHTML = `<div class="bubble"><p>${escapeHtml(message)}</p></div>`;
    elements.messages.appendChild(article);
    scrollMessages();
}

function renderMarkdownInto(target, markdown) {
    target.classList.add("markdown-body");
    target.innerHTML = renderMarkdown(markdown || "");
}

function scrollMessages() {
    elements.messages.scrollTop = elements.messages.scrollHeight;
}

function escapeHtml(value) {
    return value
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");
}

function renderInlineMarkdown(text) {
    return escapeHtml(text)
        .replace(/`([^`]+)`/g, "<code>$1</code>")
        .replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>")
        .replace(/\*([^*]+)\*/g, "<em>$1</em>");
}

function renderMarkdown(markdown) {
    const normalized = markdown.replace(/\r/g, "").trim();
    if (!normalized) {
        return "<p>No content.</p>";
    }

    const lines = normalized.split("\n");
    const blocks = [];
    let i = 0;

    while (i < lines.length) {
        const line = lines[i];
        const trimmed = line.trim();

        if (!trimmed) {
            i += 1;
            continue;
        }

        if (trimmed.startsWith("```")) {
            const codeLines = [];
            const language = trimmed.slice(3).trim();
            i += 1;
            while (i < lines.length && !lines[i].trim().startsWith("```")) {
                codeLines.push(lines[i]);
                i += 1;
            }
            i += 1;
            blocks.push(`<pre><code class="language-${escapeHtml(language)}">${escapeHtml(codeLines.join("\n"))}</code></pre>`);
            continue;
        }

        if (/^#{1,4}\s+/.test(trimmed)) {
            const level = trimmed.match(/^#+/)[0].length;
            const content = trimmed.replace(/^#{1,4}\s+/, "");
            blocks.push(`<h${level}>${renderInlineMarkdown(content)}</h${level}>`);
            i += 1;
            continue;
        }

        if (trimmed.startsWith(">")) {
            const quoteLines = [];
            while (i < lines.length && lines[i].trim().startsWith(">")) {
                quoteLines.push(lines[i].trim().replace(/^>\s?/, ""));
                i += 1;
            }
            blocks.push(`<blockquote>${quoteLines.map(renderInlineMarkdown).join("<br>")}</blockquote>`);
            continue;
        }

        if (isTableStart(lines, i)) {
            const tableLines = [];
            while (i < lines.length && lines[i].includes("|")) {
                tableLines.push(lines[i]);
                i += 1;
            }
            blocks.push(renderTable(tableLines));
            continue;
        }

        if (/^[-*]\s+/.test(trimmed)) {
            const items = [];
            while (i < lines.length && /^[-*]\s+/.test(lines[i].trim())) {
                items.push(`<li>${renderInlineMarkdown(lines[i].trim().replace(/^[-*]\s+/, ""))}</li>`);
                i += 1;
            }
            blocks.push(`<ul>${items.join("")}</ul>`);
            continue;
        }

        if (/^\d+\.\s+/.test(trimmed)) {
            const items = [];
            while (i < lines.length && /^\d+\.\s+/.test(lines[i].trim())) {
                items.push(`<li>${renderInlineMarkdown(lines[i].trim().replace(/^\d+\.\s+/, ""))}</li>`);
                i += 1;
            }
            blocks.push(`<ol>${items.join("")}</ol>`);
            continue;
        }

        const paragraphLines = [];
        while (i < lines.length && lines[i].trim() && !isSpecialBlock(lines, i)) {
            paragraphLines.push(lines[i].trim());
            i += 1;
        }
        blocks.push(`<p>${renderInlineMarkdown(paragraphLines.join(" "))}</p>`);
    }

    return blocks.join("");
}

function isSpecialBlock(lines, index) {
    const trimmed = lines[index].trim();
    return trimmed.startsWith("```")
        || /^#{1,4}\s+/.test(trimmed)
        || trimmed.startsWith(">")
        || /^[-*]\s+/.test(trimmed)
        || /^\d+\.\s+/.test(trimmed)
        || isTableStart(lines, index);
}

function isTableStart(lines, index) {
    if (index + 1 >= lines.length) {
        return false;
    }
    return lines[index].includes("|") && /^\s*\|?\s*:?-{3,}/.test(lines[index + 1]);
}

function renderTable(tableLines) {
    const rows = tableLines.map((line) => splitTableRow(line));
    const header = rows[0] || [];
    const body = rows.slice(2);
    const headHtml = `<thead><tr>${header.map((cell) => `<th>${renderInlineMarkdown(cell)}</th>`).join("")}</tr></thead>`;
    const bodyHtml = `<tbody>${body.map((row) => `<tr>${row.map((cell) => `<td>${renderInlineMarkdown(cell)}</td>`).join("")}</tr>`).join("")}</tbody>`;
    return `<table>${headHtml}${bodyHtml}</table>`;
}

function splitTableRow(line) {
    return line
        .trim()
        .replace(/^\|/, "")
        .replace(/\|$/, "")
        .split("|")
        .map((cell) => cell.trim());
}

function getTechnicalInterviewRoles() {
    return [elements.technicalInterviewRole.value];
}

function getFormState() {
    return {
        workflow: elements.workflow.value,
        context: elements.context.value,
        maxIterationsPerAgent: Number.parseInt(elements.iterations.value, 10) || 10,
        jobDescription: elements.jobDescription.value,
        candidateCv: elements.candidateCv.value,
        targetSeniority: elements.targetSeniority.value,
        technicalInterviewRole: elements.technicalInterviewRole.value,
        autoApproveInterviewSchedule: elements.autoApproveInterviewSchedule.checked
    };
}

function buildPayload(prompt) {
    const formState = getFormState();
    const payload = {
        projectBrief: prompt,
        context: formState.context.trim(),
        maxIterationsPerAgent: formState.maxIterationsPerAgent,
        workflow: formState.workflow
    };

    if (formState.workflow === "hiring") {
        payload.jobDescription = formState.jobDescription.trim();
        payload.candidateCv = formState.candidateCv.trim();
        payload.targetSeniority = formState.targetSeniority;
        payload.technicalInterviewRoles = getTechnicalInterviewRoles();
    }

    return payload;
}

function buildHiringStartPayload(prompt) {
    const formState = getFormState();
    return {
        projectBrief: prompt,
        jobDescription: formState.jobDescription.trim(),
        candidateCv: formState.candidateCv.trim(),
        context: formState.context.trim(),
        targetSeniority: formState.targetSeniority,
        technicalInterviewRole: formState.technicalInterviewRole,
        autoApproveInterviewSchedule: formState.autoApproveInterviewSchedule
    };
}

function persistFormState() {
    localStorage.setItem(FORM_STORAGE_KEY, JSON.stringify({
        ...getFormState(),
        prompt: elements.prompt.value
    }));
}

function restoreFormState() {
    const raw = localStorage.getItem(FORM_STORAGE_KEY);
    if (!raw) {
        refreshSampleRequest();
        return;
    }

    try {
        const formState = JSON.parse(raw);
        elements.workflow.value = formState.workflow || "delivery";
        elements.iterations.value = String(formState.maxIterationsPerAgent || 10);
        elements.context.value = formState.context || "";
        elements.jobDescription.value = formState.jobDescription || "";
        elements.candidateCv.value = formState.candidateCv || "";
        elements.targetSeniority.value = formState.targetSeniority || "AUTO";
        const legacyTechnicalRoles = Array.isArray(formState.technicalInterviewRoles)
            ? formState.technicalInterviewRoles
            : [];
        elements.technicalInterviewRole.value = formState.technicalInterviewRole
            || legacyTechnicalRoles[0]
            || "DEV";
        elements.autoApproveInterviewSchedule.checked = formState.autoApproveInterviewSchedule ?? true;
        elements.prompt.value = formState.prompt || "";
    }
    catch {
        localStorage.removeItem(FORM_STORAGE_KEY);
    }

    syncHiringPanel();
    refreshSampleRequest();
}

function saveConversationHistory() {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(conversationHistory.slice(-12)));
}

function loadConversationHistory() {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) {
        renderHistoryList();
        return;
    }

    try {
        conversationHistory = JSON.parse(raw);
    }
    catch {
        conversationHistory = [];
        localStorage.removeItem(STORAGE_KEY);
    }

    if (conversationHistory.length > 0) {
        elements.messages.innerHTML = "";
    }

    for (const item of conversationHistory) {
        appendUserMessage(item.prompt);
        if (item.type === "result") {
            appendAssistantResult(item.payload);
        } else {
            appendError(item.message);
        }
    }
    renderHistoryList();
}

function addHistoryEntry(entry) {
    conversationHistory.push(entry);
    conversationHistory = conversationHistory.slice(-12);
    saveConversationHistory();
    renderHistoryList();
}

function renderHistoryList() {
    elements.historyList.innerHTML = "";
    if (conversationHistory.length === 0) {
        elements.historyList.innerHTML = '<p class="hint">No local history yet.</p>';
        return;
    }

    const items = [...conversationHistory].reverse();
    for (const item of items) {
        const fragment = elements.historyItemTemplate.content.cloneNode(true);
        const button = fragment.querySelector(".history-item");
        fragment.querySelector(".history-item-title").textContent = item.prompt.slice(0, 72);
        fragment.querySelector(".history-item-meta").textContent = `${item.workflow.toUpperCase()} · ${item.createdAt}`;
        button.addEventListener("click", () => loadHistoryEntry(item));
        elements.historyList.appendChild(fragment);
    }
}

function loadHistoryEntry(entry) {
    applyPresetState({
        workflow: entry.workflow,
        prompt: entry.prompt,
        context: entry.payloadSent?.context || "",
        maxIterationsPerAgent: entry.payloadSent?.maxIterationsPerAgent || 10,
        jobDescription: entry.payloadSent?.jobDescription || "",
        candidateCv: entry.payloadSent?.candidateCv || "",
        targetSeniority: entry.payloadSent?.targetSeniority || "AUTO",
        technicalInterviewRole: entry.payloadSent?.technicalInterviewRole || "DEV",
        autoApproveInterviewSchedule: entry.payloadSent?.autoApproveInterviewSchedule ?? true
    });
}

function clearConversationHistory() {
    conversationHistory = [];
    localStorage.removeItem(STORAGE_KEY);
    elements.messages.innerHTML = `
        <article class="message assistant">
            <div class="bubble">
                <p>Describe the project or hiring need, and I will call the orchestrator for you.</p>
            </div>
        </article>`;
    renderHistoryList();
}

function applyPresetState(preset) {
    elements.workflow.value = preset.workflow;
    elements.prompt.value = preset.prompt || "";
    elements.context.value = preset.context || "";
    elements.iterations.value = String(preset.maxIterationsPerAgent || 10);
    elements.jobDescription.value = preset.jobDescription || "";
    elements.candidateCv.value = preset.candidateCv || "";
    elements.targetSeniority.value = preset.targetSeniority || "AUTO";
    elements.technicalInterviewRole.value = preset.technicalInterviewRole || "DEV";
    elements.autoApproveInterviewSchedule.checked = preset.autoApproveInterviewSchedule ?? true;
    syncHiringPanel();
    refreshSampleRequest();
    persistFormState();
}

function refreshSampleRequest() {
    const prompt = elements.prompt.value.trim() || "Describe the work you want the agent to do.";
    const payload = elements.workflow.value === "hiring"
        ? buildHiringStartPayload(prompt)
        : buildPayload(prompt);
    elements.sampleRequest.textContent = JSON.stringify(payload, null, 2);
}

async function copySampleRequest() {
    try {
        await navigator.clipboard.writeText(elements.sampleRequest.textContent);
        elements.status.textContent = "Sample request copied.";
    }
    catch {
        elements.status.textContent = "Clipboard unavailable in this browser context.";
    }
}

function buildSessionMarkdown(session) {
    return `## Hiring Session Update

**Stage:** ${session.stage}

**Status:** ${session.statusSummary}

**Seniority Target:** ${session.seniorityLevel || "AUTO"}

**Current Speaker:** ${session.currentSpeaker}

**Current Prompt:** ${session.currentPrompt}

**Screening Fit Score:** ${Number(session.screeningFitScore || 0).toFixed(1)}

**Interview Score:** ${Number(session.interviewScore || 0).toFixed(1)}

**Participants:** ${(session.participants || []).join(", ") || "HR"}`;
}

function updateComposerState() {
    const isHiring = elements.workflow.value === "hiring";
    const isInterviewActive = isHiring && activeHiringSession && activeHiringSession.stage === "interview_active";
    const isWaitingApproval = isHiring && activeHiringSession && activeHiringSession.requiresUserApproval;

    if (isInterviewActive) {
        elements.prompt.placeholder = "You are the candidate now. Type your interview answer here...";
        elements.sendButton.textContent = "Send Reply";
    } else if (isHiring) {
        elements.prompt.placeholder = "Describe the hiring need to start HR screening...";
        elements.sendButton.textContent = activeHiringSession ? "Refresh / Continue" : "Start Hiring";
    } else {
        elements.prompt.placeholder = "Ask PM Agent to plan a release, screen a candidate, or prepare interviews...";
        elements.sendButton.textContent = "Send";
    }

    elements.prompt.disabled = Boolean(isWaitingApproval);
}

function setActiveHiringSession(session) {
    activeHiringSession = session;
    if (session) {
        localStorage.setItem(ACTIVE_SESSION_STORAGE_KEY, session.sessionId);
    } else {
        localStorage.removeItem(ACTIVE_SESSION_STORAGE_KEY);
    }
    renderSessionPanel();
    updateComposerState();
}

function renderSessionPanel() {
    const session = activeHiringSession;
    const isVisible = elements.workflow.value === "hiring" && session;
    elements.sessionPanel.hidden = !isVisible;
    elements.timelinePanel.hidden = !isVisible;

    if (!isVisible) {
        elements.approvalRow.hidden = true;
        elements.exportNotesButton.disabled = true;
        elements.timelineList.innerHTML = "";
        return;
    }

    elements.sessionStage.textContent = session.stage;
    elements.sessionSpeaker.textContent = session.currentSpeaker || "waiting";
    elements.sessionSummary.textContent = `${session.statusSummary} Current prompt: ${session.currentPrompt}`;
    elements.approvalRow.hidden = !session.requiresUserApproval;
    elements.exportNotesButton.disabled = !session.notesDocumentPath;
    renderTimeline(session.transcript || []);
}

function renderTimeline(transcript) {
    elements.timelineList.innerHTML = "";
    if (!transcript || transcript.length === 0) {
        elements.timelineList.innerHTML = '<p class="hint">Timeline will appear after the hiring session starts.</p>';
        return;
    }

    for (const turn of transcript) {
        const fragment = elements.timelineItemTemplate.content.cloneNode(true);
        fragment.querySelector(".timeline-speaker").textContent = turn.speaker;
        fragment.querySelector(".timeline-time").textContent = new Date(turn.occurredAt).toLocaleTimeString();
        renderMarkdownInto(fragment.querySelector(".timeline-message"), turn.message);
        elements.timelineList.appendChild(fragment);
    }
}

function persistActiveSession() {
    if (activeHiringSession) {
        localStorage.setItem(ACTIVE_SESSION_STORAGE_KEY, activeHiringSession.sessionId);
    }
}

async function restoreActiveSession() {
    const sessionId = localStorage.getItem(ACTIVE_SESSION_STORAGE_KEY);
    if (!sessionId) {
        return;
    }

    try {
        const response = await fetch(`/api/hiring/sessions/${sessionId}`);
        if (response.ok) {
            const session = await response.json();
            setActiveHiringSession(session);
        } else {
            localStorage.removeItem(ACTIVE_SESSION_STORAGE_KEY);
        }
    }
    catch {
        localStorage.removeItem(ACTIVE_SESSION_STORAGE_KEY);
    }
}

async function startHiringSession(prompt) {
    const payload = buildHiringStartPayload(prompt);
    const response = await fetch("/api/hiring/sessions", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
    });

    if (!response.ok) {
        throw new Error(await response.text() || `Request failed with status ${response.status}`);
    }

    const session = await response.json();
    setActiveHiringSession(session);
    appendSessionUpdate(session);
    addHistoryEntry({
        type: "session",
        workflow: "hiring",
        prompt,
        sessionId: session.sessionId,
        payloadSent: payload,
        session,
        createdAt: new Date().toLocaleString()
    });
    persistActiveSession();
}

async function sendCandidateResponse(prompt) {
    const response = await fetch(`/api/hiring/sessions/${activeHiringSession.sessionId}/candidate-response`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ message: prompt })
    });

    if (!response.ok) {
        throw new Error(await response.text() || `Request failed with status ${response.status}`);
    }

    const session = await response.json();
    setActiveHiringSession(session);
    appendSessionUpdate(session);
    addHistoryEntry({
        type: "session",
        workflow: "hiring",
        prompt,
        sessionId: session.sessionId,
        payloadSent: { message: prompt },
        session,
        createdAt: new Date().toLocaleString()
    });
    persistActiveSession();
}

async function submitApproval(approved) {
    if (!activeHiringSession || !activeHiringSession.requiresUserApproval) {
        return;
    }

    const route = activeHiringSession.approvalType === "interview_schedule"
        ? "approve-interview"
        : "approve-screening";

    elements.status.textContent = approved ? "Submitting approval..." : "Submitting rejection...";

    const response = await fetch(`/api/hiring/sessions/${activeHiringSession.sessionId}/${route}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ approved, comment: approved ? "Approved from browser UI." : "Rejected from browser UI." })
    });

    if (!response.ok) {
        appendError(await response.text() || "Approval request failed.");
        return;
    }

    const session = await response.json();
    setActiveHiringSession(session);
    appendSessionUpdate(session);
    addHistoryEntry({
        type: "session",
        workflow: "hiring",
        prompt: approved ? "Approved session step" : "Rejected session step",
        sessionId: session.sessionId,
        payloadSent: { approved },
        session,
        createdAt: new Date().toLocaleString()
    });
    elements.status.textContent = approved ? "Approval submitted." : "Rejection submitted.";
}

async function exportNotes() {
    if (!activeHiringSession) {
        return;
    }

    const response = await fetch(`/api/hiring/sessions/${activeHiringSession.sessionId}/notes`);
    if (!response.ok) {
        elements.status.textContent = "Notes are not available yet.";
        return;
    }

    const blob = await response.blob();
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `hiring-session-${activeHiringSession.sessionId}.md`;
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
    elements.status.textContent = "Notes exported.";
}

async function hydrateTextareaFromFile(file, target) {
    if (!file) {
        return;
    }

    try {
        const text = await file.text();
        target.value = text;
        refreshSampleRequest();
        persistFormState();
        elements.status.textContent = `${file.name} loaded.`;
    }
    catch {
        elements.status.textContent = `Could not read ${file.name}. Please use a text-based file.`;
    }
}

async function handleSubmit(event) {
    event.preventDefault();

    const prompt = elements.prompt.value.trim();
    if (!prompt) {
        return;
    }

    const workflow = elements.workflow.value;

    appendUserMessage(prompt);
    elements.prompt.value = "";
    elements.sendButton.disabled = true;
    elements.status.textContent = workflow === "hiring" ? "Running hiring workflow..." : "Running orchestrator...";

    try {
        if (workflow === "hiring") {
            if (activeHiringSession && activeHiringSession.stage === "interview_active") {
                await sendCandidateResponse(prompt);
            } else if (!activeHiringSession || ["completed", "rejected"].includes(activeHiringSession.stage)) {
                await startHiringSession(prompt);
            } else {
                appendError("The hiring session is waiting for approval. Use the Approve or Reject buttons.");
            }
        } else {
            const payload = buildPayload(prompt);
            const response = await fetch("/api/orchestrator/run", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify(payload)
            });

            if (!response.ok) {
                const errorText = await response.text();
                throw new Error(errorText || `Request failed with status ${response.status}`);
            }

            const result = await response.json();
            appendAssistantResult(result);
            addHistoryEntry({
                type: "result",
                workflow,
                prompt,
                payload: result,
                payloadSent: payload,
                createdAt: new Date().toLocaleString(),
                payloadSummary: result.summary
            });
        }
        elements.status.textContent = "Completed.";
    }
    catch (error) {
        appendError(error.message || "Unknown error.");
        addHistoryEntry({
            type: "error",
            workflow,
            prompt,
            message: error.message || "Unknown error.",
            payloadSent: elements.workflow.value === "hiring" ? buildHiringStartPayload(prompt) : buildPayload(prompt),
            createdAt: new Date().toLocaleString()
        });
        elements.status.textContent = "Request failed.";
    }
    finally {
        elements.sendButton.disabled = false;
        refreshSampleRequest();
        persistFormState();
    }
}

function registerFormPersistence() {
    [
        elements.workflow,
        elements.iterations,
        elements.context,
        elements.jobDescription,
        elements.candidateCv,
        elements.targetSeniority,
        elements.technicalInterviewRole,
        elements.autoApproveInterviewSchedule,
        elements.prompt
    ].forEach((element) => {
        element.addEventListener("input", () => {
            refreshSampleRequest();
            persistFormState();
        });
        element.addEventListener("change", () => {
            refreshSampleRequest();
            persistFormState();
        });
    });
}

function registerPresets() {
    document.querySelectorAll(".preset-button").forEach((button) => {
        button.addEventListener("click", () => {
            applyPresetState(presets[button.dataset.preset]);
        });
    });
}

function restoreConversationEntry(item) {
    appendUserMessage(item.prompt);
    if (item.type === "result") {
        appendAssistantResult(item.payload);
    } else if (item.type === "session") {
        appendSessionUpdate(item.session);
    } else {
        appendError(item.message);
    }
}

function bindFileUploads() {
    elements.jobDescriptionFile.addEventListener("change", async () => {
        await hydrateTextareaFromFile(elements.jobDescriptionFile.files?.[0], elements.jobDescription);
        elements.jobDescriptionFile.value = "";
    });
    elements.candidateCvFile.addEventListener("change", async () => {
        await hydrateTextareaFromFile(elements.candidateCvFile.files?.[0], elements.candidateCv);
        elements.candidateCvFile.value = "";
    });
}

const originalLoadConversationHistory = loadConversationHistory;
loadConversationHistory = function patchedLoadConversationHistory() {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) {
        renderHistoryList();
        return;
    }

    try {
        conversationHistory = JSON.parse(raw);
    }
    catch {
        conversationHistory = [];
        localStorage.removeItem(STORAGE_KEY);
    }

    if (conversationHistory.length > 0) {
        elements.messages.innerHTML = "";
    }

    for (const item of conversationHistory) {
        restoreConversationEntry(item);
    }
    renderHistoryList();
};

elements.workflow.addEventListener("change", syncHiringPanel);
elements.form.addEventListener("submit", handleSubmit);
elements.copySampleButton.addEventListener("click", copySampleRequest);
elements.clearHistoryButton.addEventListener("click", clearConversationHistory);
elements.approveButton.addEventListener("click", () => submitApproval(true));
elements.rejectButton.addEventListener("click", () => submitApproval(false));
elements.exportNotesButton.addEventListener("click", exportNotes);

registerPresets();
registerFormPersistence();
bindFileUploads();
restoreFormState();
loadConversationHistory();
restoreActiveSession().finally(syncHiringPanel);