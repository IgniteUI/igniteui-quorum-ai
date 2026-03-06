#!/usr/bin/env node
'use strict';

/**
 * MAKER MCP bootstrap
 *
 * On first run for a given version + platform:
 *   1. Downloads maker-mcp-{rid}.tar.gz from GitHub Releases
 *   2. Extracts it to ~/.cache/maker-mcp/{version}/{rid}/
 *   3. Spawns the binary with inherited stdio (MCP STDIO protocol)
 *
 * Subsequent runs skip the download and use the cached binary.
 */

const https    = require('https');
const fs       = require('fs');
const path     = require('path');
const os       = require('os');
const { execSync, spawn } = require('child_process');

const GITHUB_REPO = 'IgniteUI/MAKER';
const VERSION     = require('../package.json').version;

// ── Platform detection ──────────────────────────────────────────────────────

function getRid() {
  const p = process.platform;
  const a = process.arch;

  if (p === 'win32'  && a === 'x64')   return 'win-x64';
  if (p === 'darwin' && a === 'x64')   return 'osx-x64';
  if (p === 'darwin' && a === 'arm64') return 'osx-arm64';
  if (p === 'linux'  && a === 'x64')   return 'linux-x64';

  throw new Error(
    `[maker-mcp] Unsupported platform: ${p}-${a}.\n` +
    `Supported: win-x64, osx-x64, osx-arm64, linux-x64`
  );
}

function getBinaryName(rid) {
  return rid.startsWith('win') ? 'maker-mcp.exe' : 'maker-mcp';
}

// ── Cache location ──────────────────────────────────────────────────────────

function getCacheDir(rid) {
  const base =
    process.env.MAKER_MCP_CACHE ||
    path.join(
      process.platform === 'win32'
        ? (process.env.LOCALAPPDATA || path.join(os.homedir(), 'AppData', 'Local'))
        : path.join(os.homedir(), '.cache'),
      'maker-mcp',
      VERSION,
      rid
    );
  return base;
}

// ── Download helpers ────────────────────────────────────────────────────────

function download(url, destFile) {
  return new Promise((resolve, reject) => {
    const file = fs.createWriteStream(destFile);

    function get(url) {
      https.get(url, { headers: { 'User-Agent': 'maker-mcp-bootstrap' } }, res => {
        // Follow redirects (GitHub Releases uses them)
        if (res.statusCode === 301 || res.statusCode === 302) {
          get(res.headers.location);
          return;
        }
        if (res.statusCode !== 200) {
          file.close();
          fs.rmSync(destFile, { force: true });
          reject(new Error(`HTTP ${res.statusCode} while downloading ${url}`));
          return;
        }
        res.pipe(file);
        file.on('finish', () => file.close(resolve));
        file.on('error', err => { fs.rmSync(destFile, { force: true }); reject(err); });
      }).on('error', err => { fs.rmSync(destFile, { force: true }); reject(err); });
    }

    get(url);
  });
}

function extract(tarball, destDir) {
  fs.mkdirSync(destDir, { recursive: true });
  execSync(`tar -xzf "${tarball}" -C "${destDir}"`, { stdio: 'pipe' });
}

// ── Main ────────────────────────────────────────────────────────────────────

async function ensureBinary(rid) {
  const cacheDir    = getCacheDir(rid);
  const binaryName  = getBinaryName(rid);
  const binaryPath  = path.join(cacheDir, binaryName);

  if (fs.existsSync(binaryPath)) return binaryPath;

  const archiveName = `maker-mcp-${rid}.tar.gz`;
  const downloadUrl = `https://github.com/${GITHUB_REPO}/releases/download/v${VERSION}/${archiveName}`;
  const tempFile    = path.join(os.tmpdir(), `${archiveName}.${Date.now()}.tmp`);

  process.stderr.write(`[maker-mcp] First run — downloading v${VERSION} for ${rid}...\n`);

  await download(downloadUrl, tempFile);

  process.stderr.write(`[maker-mcp] Extracting to cache...\n`);
  extract(tempFile, cacheDir);
  fs.rmSync(tempFile, { force: true });

  if (process.platform !== 'win32') {
    fs.chmodSync(binaryPath, 0o755);
  }

  process.stderr.write(`[maker-mcp] Ready.\n`);
  return binaryPath;
}

async function main() {
  const rid        = getRid();
  const binaryPath = await ensureBinary(rid);

  const child = spawn(binaryPath, process.argv.slice(2), {
    stdio: 'inherit',
    // Set cwd to the cache dir so the binary can find MAKER/AI/Instructions/
    cwd: path.dirname(binaryPath),
    env: process.env
  });

  child.on('error', err => {
    process.stderr.write(`[maker-mcp] Failed to start binary: ${err.message}\n`);
    if (process.platform === 'darwin') {
      process.stderr.write(
        `[maker-mcp] On macOS you may need to remove the quarantine flag:\n` +
        `  xattr -d com.apple.quarantine "${binaryPath}"\n`
      );
    }
    process.exit(1);
  });

  child.on('exit', code => process.exit(code ?? 0));
}

main().catch(err => {
  process.stderr.write(`[maker-mcp] Error: ${err.message}\n`);
  process.exit(1);
});
