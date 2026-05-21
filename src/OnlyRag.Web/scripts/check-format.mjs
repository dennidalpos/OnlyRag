import { readdir, readFile } from "node:fs/promises";
import path from "node:path";

const roots = ["src", "e2e", "vite.config.ts", "playwright.config.ts", "tsconfig.json", "index.html"];
const checkedExtensions = new Set([".ts", ".tsx", ".css", ".json", ".html"]);
const ignoredDirectories = new Set(["dist", "node_modules"]);
const failures = [];

for (const root of roots) {
  await checkPath(path.resolve(root));
}

if (failures.length > 0) {
  for (const failure of failures) {
    console.error(failure);
  }
  process.exit(1);
}

console.log("Text format check passed.");

async function checkPath(filePath) {
  const entries = await readdir(path.dirname(filePath), { withFileTypes: true });
  const name = path.basename(filePath);
  const entry = entries.find((candidate) => candidate.name === name);
  if (!entry) {
    return;
  }

  if (entry.isDirectory()) {
    await checkDirectory(filePath);
    return;
  }

  await checkFile(filePath);
}

async function checkDirectory(directoryPath) {
  const entries = await readdir(directoryPath, { withFileTypes: true });
  for (const entry of entries) {
    if (ignoredDirectories.has(entry.name)) {
      continue;
    }

    const childPath = path.join(directoryPath, entry.name);
    if (entry.isDirectory()) {
      await checkDirectory(childPath);
    } else {
      await checkFile(childPath);
    }
  }
}

async function checkFile(filePath) {
  if (!checkedExtensions.has(path.extname(filePath))) {
    return;
  }

  const relativePath = path.relative(process.cwd(), filePath);
  const content = await readFile(filePath, "utf8");
  if (!content.endsWith("\n")) {
    failures.push(`${relativePath}: missing final newline`);
  }

  const lines = content.split(/\r?\n/);
  for (const [index, line] of lines.entries()) {
    if (/[ \t]$/.test(line)) {
      failures.push(`${relativePath}:${index + 1}: trailing whitespace`);
    }
  }
}
