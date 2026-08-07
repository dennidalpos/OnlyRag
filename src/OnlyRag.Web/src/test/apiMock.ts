import { vi } from "vitest";

export type MockApiCall = {
  path: string;
  method: string;
  headers: Headers;
  body: BodyInit | null | undefined;
};

type MockApiRequest = MockApiCall & {
  url: URL;
};

type MockApiResponse = {
  body?: unknown;
  status?: number;
  headers?: Record<string, string>;
};

export type MockApiRoute = {
  path: string | RegExp;
  method?: string;
  response?: unknown;
  status?: number;
  handler?: (request: MockApiRequest) => MockApiResponse | Promise<MockApiResponse>;
};

export function mockApi(routes: MockApiRoute[]) {
  const calls: MockApiCall[] = [];

  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const rawUrl = typeof input === "string" ? input : input instanceof URL ? input.toString() : input.url;
    const url = new URL(rawUrl);
    const method = (init?.method ?? "GET").toUpperCase();
    const path = `${url.pathname}${url.search}`;
    const headers = new Headers(init?.headers);
    const body = init?.body;
    calls.push({ path, method, headers, body });

    const defaultRoutes: MockApiRoute[] = [
      {
        path: /\/hubs\/.*\/negotiate.*/,
        method: "POST",
        response: {
          connectionId: "mock-connection-id",
          availableTransports: [
            { transport: "WebSockets", transferFormats: ["Text"] },
            { transport: "ServerSentEvents", transferFormats: ["Text"] },
            { transport: "LongPolling", transferFormats: ["Text"] }
          ]
        }
      }
    ];

    const allRoutes = [...routes, ...defaultRoutes];
    const route = allRoutes.find(
      (candidate) =>
        (candidate.method?.toUpperCase() ?? "GET") === method &&
        (typeof candidate.path === "string" ? candidate.path === path : candidate.path.test(path))
    );

    if (!route) {
      return jsonResponse({ detail: `Unhandled API route: ${method} ${path}` }, 404);
    }

    const result = route.handler
      ? await route.handler({ path, method, headers, body, url })
      : { body: route.response, status: route.status };

    return jsonResponse(result.body, result.status ?? route.status ?? 200, result.headers);
  });

  vi.stubGlobal("fetch", fetchMock);
  return { calls, fetchMock };
}

function jsonResponse(body: unknown, status: number, extraHeaders: Record<string, string> = {}): Response {
  if (status === 204) {
    return new Response(null, { status, headers: extraHeaders });
  }

  if (typeof body === "string") {
    return new Response(new TextEncoder().encode(body), {
      status,
      headers: {
        "Content-Type": "text/event-stream",
        ...extraHeaders
      }
    });
  }

  return new Response(JSON.stringify(body ?? null), {
    status,
    headers: {
      "Content-Type": "application/json",
      ...extraHeaders
    }
  });
}
