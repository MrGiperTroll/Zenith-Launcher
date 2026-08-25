/**
 * Cloudflare Worker for Zenith Launcher – Ely.by OAuth2 token exchange.
 *
 * Endpoints:
 *   POST /token    {"code":"...","redirect_uri":"http://localhost:7356/auth/callback"}
 *   POST /refresh  {"refresh_token":"..."}
 *   GET  /health   -> liveness probe, returns {"status":"ok"}
 *
 * Required Secrets (Workers → Settings → Variables and Secrets):
 *   ELY_CLIENT_ID        – public client ID from Ely.by app registration
 *   ELY_CLIENT_SECRET    – matching client secret
 *
 * Deploy (dashboard): Workers & Pages → Create Worker → paste this file.
 * Deploy (CLI):   npx wrangler deploy
 * Set secrets:    npx wrangler secret put ELY_CLIENT_ID
 *                 npx wrangler secret put ELY_CLIENT_SECRET
 */

const UPSTREAM_TOKEN_URL = "https://account.ely.by/api/oauth2/v1/token";
const DEFAULT_REDIRECT_URI = "http://localhost:7356/auth/callback";
const REFRESH_SCOPE = "minecraft_server_session account_info offline_access";

// Per‑origin sliding‑window rate limiter (approx. – Workers isolates are ephemeral).
const hits = new Map(); // ip → number[]

function rateLimited(ip, max, windowMs) {
	const now = Date.now();
	let arr = hits.get(ip);
	if (!arr) {
		arr = [];
		hits.set(ip, arr);
	}
	while (arr.length && now - arr[0] > windowMs) arr.shift();
	if (arr.length >= max) return true;
	arr.push(now);
	if (hits.size > 10000) {
		for (const [k, v] of hits) {
			if (!v.length || now - v[v.length - 1] > windowMs) hits.delete(k);
		}
	}
	return false;
}

function json(status, body) {
	return new Response(JSON.stringify(body), {
		status,
		headers: { "Content-Type": "application/json; charset=utf-8" },
	});
}

function fieldStr(value, maxLen) {
	return typeof value === "string" && value.length > 0 && value.length <= maxLen ? value : null;
}

async function readJsonBody(request) {
	const ct = (request.headers.get("Content-Type") || "").toLowerCase();
	if (!ct.includes("application/json"))
		throw new Error("content_type_must_be_json");
	const text = await request.text();
	if (!text || text.length > 4096) throw new Error("body_too_large");
	let parsed;
	try {
		parsed = JSON.parse(text);
	} catch {
		throw new Error("malformed_json");
	}
	if (parsed === null || typeof parsed !== "object" || Array.isArray(parsed))
		throw new Error("malformed_json");
	return parsed;
}

async function exchange(clientId, clientSecret, params) {
	const upstream = await fetch(UPSTREAM_TOKEN_URL, {
		method: "POST",
		headers: { "Content-Type": "application/x-www-form-urlencoded" },
		body: new URLSearchParams(params).toString(),
	});
	const body = await upstream.text();
	return new Response(body, {
		status: upstream.status,
		headers: { "Content-Type": "application/json; charset=utf-8" },
	});
}

// ---------------------------------------------------------------------------

export default {
	async fetch(request, env, ctx) {
		const ip = request.headers.get("CF-Connecting-IP") || "unknown";
		const max = parseInt(env.RATE_LIMIT_MAX || "30", 10) || 30;
		const windowMs = parseInt(env.RATE_LIMIT_WINDOW_MS || "60000", 10) || 60000;

		// Secrets live exclusively in the Worker's environment.
		const clientId = env.ELY_CLIENT_ID;
		const clientSecret = env.ELY_CLIENT_SECRET;

		const url = new URL(request.url);
		const path = url.pathname.replace(/\/+$/, "");

		if (path === "/health") return json(200, { status: "ok" });

		if (request.method !== "POST") return json(405, { error: "method_not_allowed" });

		// Reject calls from browsers that set an Origin header.
		if (request.headers.get("Origin")) return json(403, { error: "forbidden" });

		if (rateLimited(ip, max, windowMs))
			return json(429, { error: "rate_limited", retry_after_ms: windowMs });

		// If either secret is missing, return a clear 500 instead of crashing.
		if (!clientId || !clientSecret)
			return json(500, {
				error: "worker_not_configured",
				detail: "ELY_CLIENT_ID / ELY_CLIENT_SECRET secrets are missing.",
			});

		let bodyParams;
		try {
			bodyParams = await readJsonBody(request);
		} catch (e) {
			return json(400, { error: "invalid_request", detail: e.message });
		}

		const redirectUri = fieldStr(bodyParams.redirect_uri, 512) ?? DEFAULT_REDIRECT_URI;
		const allowedRedirect = env.ALLOWED_REDIRECT_URI || DEFAULT_REDIRECT_URI;
		if (redirectUri !== allowedRedirect)
			return json(403, { error: "redirect_uri_not_allowed" });

		if (path === "/token") {
			const code = fieldStr(bodyParams.code, 512);
			if (!code) return json(400, { error: "invalid_request", detail: "missing or invalid 'code'" });
			return exchange(clientId, clientSecret, {
				grant_type: "authorization_code",
				client_id: clientId,
				client_secret: clientSecret,
				redirect_uri: redirectUri,
				code,
			});
		}

		if (path === "/refresh") {
			const refreshToken = fieldStr(bodyParams.refresh_token, 2048);
			if (!refreshToken)
				return json(400, { error: "invalid_request", detail: "missing or invalid 'refresh_token'" });
			return exchange(clientId, clientSecret, {
				grant_type: "refresh_token",
				client_id: clientId,
				client_secret: clientSecret,
				scope: REFRESH_SCOPE,
				refresh_token: refreshToken,
			});
		}

		return json(404, { error: "not_found" });
	},
};