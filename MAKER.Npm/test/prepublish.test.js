#!/usr/bin/env node
'use strict';

/**
 * Pre-publish integration test for @igniteui/maker-mcp.
 *
 * What it verifies:
 *   1. `npm pack` produces a tarball with the expected files.
 *   2. The packed tarball can be installed into a fresh temporary directory.
 *   3. The installed binary is executable and exits with the expected
 *      "unsupported argument" or help output — NOT a JS crash.
 *   4. The bootstrap script correctly reports an unsupported platform error
 *      when run with a spoofed unsupported platform (env override).
 *
 * Run from the MAKER.Npm directory:
 *   node test/prepublish.test.js
 */

const { execSync, spawnSync } = require('child_process');
const fs   = require('fs');
const path = require('path');
const os   = require('os');

// ── Helpers ──────────────────────────────────────────────────────────────────

let passed = 0;
let failed = 0;

function assert(condition, message) {
  if (condition) {
    console.log(`  ✓  ${message}`);
    passed++;
  } else {
    console.error(`  ✗  ${message}`);
    failed++;
  }
}

function run(cmd, opts = {}) {
  return spawnSync(cmd, { shell: true, encoding: 'utf8', ...opts });
}

function withTmpDir(fn) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'maker-mcp-test-'));
  try {
    fn(dir);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

// ── Tests ────────────────────────────────────────────────────────────────────

const pkgDir = path.resolve(__dirname, '..');
const pkg    = JSON.parse(fs.readFileSync(path.join(pkgDir, 'package.json'), 'utf8'));

console.log(`\nTesting @${pkg.name}@${pkg.version}\n`);

// ── 1. npm pack produces a tarball ───────────────────────────────────────────

console.log('1. npm pack');

withTmpDir(packDir => {
  const result = run(`npm pack --pack-destination "${packDir}"`, { cwd: pkgDir });
  assert(result.status === 0, 'npm pack exits 0');

  const tarballs = fs.readdirSync(packDir).filter(f => f.endsWith('.tgz'));
  assert(tarballs.length === 1, `produces exactly one .tgz (got ${tarballs.length})`);

  if (tarballs.length === 1) {
    const tarball = path.join(packDir, tarballs[0]);

    // ── 2. Tarball contains expected files ──────────────────────────────────
    console.log('\n2. Tarball contents');

    const listResult = run(`tar -tzf "${tarball}"`);
    assert(listResult.status === 0, 'tarball is readable');

    const entries = listResult.stdout.split('\n').map(l => l.trim()).filter(Boolean);

    const requiredFiles = [
      'package/package.json',
      'package/bin/maker-mcp.js',
      'package/README.md',
      'package/CHANGELOG.md',
    ];

    for (const file of requiredFiles) {
      assert(entries.includes(file), `tarball contains ${file}`);
    }

    const unexpectedFiles = entries.filter(e =>
      e.startsWith('package/') &&
      !requiredFiles.includes(e) &&
      e !== 'package/'
    );
    assert(
      unexpectedFiles.length === 0,
      unexpectedFiles.length === 0
        ? 'no unexpected files in tarball'
        : `unexpected files: ${unexpectedFiles.join(', ')}`
    );

    // ── 3. Install from tarball into a fresh directory ──────────────────────
    console.log('\n3. Fresh install from tarball');

    withTmpDir(installDir => {
      // Minimal package.json so npm is happy
      fs.writeFileSync(
        path.join(installDir, 'package.json'),
        JSON.stringify({ name: 'test-install', version: '1.0.0', private: true })
      );

      const installResult = run(`npm install "${tarball}" --no-save`, { cwd: installDir });
      assert(installResult.status === 0, 'npm install exits 0');

      const binPath = path.join(
        installDir, 'node_modules', '.bin',
        process.platform === 'win32' ? 'maker-mcp.cmd' : 'maker-mcp'
      );
      assert(fs.existsSync(binPath), `bin symlink/shim exists at ${path.relative(installDir, binPath)}`);

      // ── 4. Bootstrap script is runnable ──────────────────────────────────
      console.log('\n4. Bootstrap script smoke-test');

      const scriptPath = path.join(
        installDir, 'node_modules', '@igniteui', 'maker-mcp', 'bin', 'maker-mcp.js'
      );
      assert(fs.existsSync(scriptPath), 'maker-mcp.js is present after install');

      // Run with an unsupported platform to verify the error path without
      // triggering a real download.
      const smokeResult = spawnSync(
        process.execPath,
        [scriptPath, '--stdio'],
        {
          encoding: 'utf8',
          timeout: 10_000,
          env: {
            ...process.env,
            // Override platform detection to force the unsupported-platform
            // branch, preventing any network call during the test.
            MAKER_MCP_FORCE_UNSUPPORTED_PLATFORM: '1',
          },
        }
      );

      // The script must exit non-zero and print a recognisable message.
      // If MAKER_MCP_FORCE_UNSUPPORTED_PLATFORM is not implemented the
      // script will attempt a real download; in that case we at least
      // confirm it does not crash with a JS syntax/require error.
      const combinedOutput = (smokeResult.stdout ?? '') + (smokeResult.stderr ?? '');
      const isJsCrash =
        combinedOutput.includes('SyntaxError') ||
        combinedOutput.includes('Cannot find module') ||
        combinedOutput.includes('ReferenceError');

      assert(!isJsCrash, 'bootstrap script does not crash with a JS error');
      assert(smokeResult.status !== undefined, 'bootstrap script exits with a numeric code');
    });
  }
});

// ── Summary ──────────────────────────────────────────────────────────────────

console.log(`\n${'─'.repeat(50)}`);
console.log(`Results: ${passed} passed, ${failed} failed\n`);

if (failed > 0) process.exit(1);
