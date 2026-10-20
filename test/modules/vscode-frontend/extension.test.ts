import { test } from 'vitest'
import { mkdir, mkdtemp, rm, writeFile, stat } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { pathToFileURL } from 'node:url'
import { execFile } from 'node:child_process'
import { promisify } from 'node:util'

/**
 * @anchor vscode-host-bootstrap
 * @anchor host-shell-logging
 * @anchor vscode-canvas-display-boundary
 * @anchor frontend-setup
 */
export class ExtensionBundleTest {
  private constructor() {}

  static {
    test('ESM 扩展产物真实激活、注册并释放 Host', async (): Promise<void> => {
      await mkdir('_build/tests', { recursive: true })
      const workspaceFullPath: string = await mkdtemp(resolve('_build/tests/extension-'))
      const settings: string = 'guid: extension\nname: 扩展验证\nmainMap: ""'
      const skill: string = '# MechToolkit\n'
      const writtenPaths: readonly string[] = [
        join(workspaceFullPath, 'setting.json'),
        join(workspaceFullPath, 'docs/MechToolkit/SKILL.md'),
        join(workspaceFullPath, '.trae/mcp.json'),
        join(workspaceFullPath, '.trae/skills/mechtoolkit/SKILL.md'),
      ]
      try {
        await writeFile(join(workspaceFullPath, 'setting.json'), settings)
        await mkdir(join(workspaceFullPath, 'docs/MechToolkit'), { recursive: true })
        await writeFile(join(workspaceFullPath, 'docs/MechToolkit/SKILL.md'), skill)
        const script: string = `
          import assert from 'node:assert/strict';
          import { registerHooks } from 'node:module';
          import { createServer } from 'node:net';
          import { readFile, readdir } from 'node:fs/promises';
          import { Window } from 'happy-dom';
          const reservation = createServer();
          await new Promise(resolve => reservation.listen(0, '127.0.0.1', resolve));
          const port = reservation.address().port;
          await new Promise(resolve => reservation.close(resolve));
          const registrations = [];
          const disposable = { dispose() {} };
          let sidebar;
          let canvas;
          let receive;
          let failureStage;
          const activationFailure = new Error('activation registration failure');
          const uri = fsPath => ({ fsPath, toString() { return fsPath; } });
          globalThis.extensionTestApi = {
            window: {
              createOutputChannel() { return { append() {}, dispose() {}, show() {} }; },
              registerCustomEditorProvider(name, provider) { registrations.push(name); canvas = provider; if (failureStage === 'custom') throw activationFailure; return disposable; },
              registerWebviewViewProvider(name, provider) { registrations.push(name); sidebar = provider; if (failureStage === 'webview') throw activationFailure; return disposable; }
            },
            workspace: {
              workspaceFolders: [{ uri: { fsPath: ${JSON.stringify(workspaceFullPath)} } }],
              getConfiguration() { return { get() { return port; } }; }
            },
            commands: { registerCommand(name) { registrations.push(name); if (failureStage === 'command') throw activationFailure; return disposable; } },
            Disposable: class { constructor(dispose) { this.dispose = dispose; } },
            Uri: { file: uri, joinPath(base, ...segments) { return uri(base.fsPath + '/' + segments.join('/')); } }
          };
          const stub = 'data:text/javascript,' + encodeURIComponent('export const window=globalThis.extensionTestApi.window; export const workspace=globalThis.extensionTestApi.workspace; export const commands=globalThis.extensionTestApi.commands; export const Disposable=globalThis.extensionTestApi.Disposable; export const Uri=globalThis.extensionTestApi.Uri; export class CompletionItem {} export class CodeLens {} export class CallHierarchyItem {} export class CodeAction {} export class DocumentLink {} export class TypeHierarchyItem {} export class Diagnostic {} export class InlayHint {} export class SymbolInformation {}');
          const osStub = 'data:text/javascript,' + encodeURIComponent('export * from "os"; export function homedir() { return ' + ${JSON.stringify(JSON.stringify(workspaceFullPath))} + '; }');
          registerHooks({ resolve(specifier, context, nextResolve) { if (specifier === 'node:os') return { url: osStub, format: 'module', shortCircuit: true }; return specifier === 'vscode' ? { url: stub, format: 'module', shortCircuit: true } : nextResolve(specifier, context); }, load(url, context, nextLoad) { return url === stub || url === osStub ? { format: 'module', source: decodeURIComponent(url.slice('data:text/javascript,'.length)), shortCircuit: true } : nextLoad(url, context); } });
          const extension = await import(${JSON.stringify(pathToFileURL(resolve('_build/frontend/extension.mjs')).href)});
          assert.equal(typeof extension.activate, 'function');
          assert.equal(typeof extension.deactivate, 'function');
          const context = { subscriptions: [], globalStorageUri: { fsPath: ${JSON.stringify(join(workspaceFullPath, 'storage'))} }, extensionUri: { fsPath: ${JSON.stringify(join(workspaceFullPath, 'src/frontend'))} }, extension: { packageJSON: { version: '0.1.0' } } };
          try {
            for (const stage of ['custom', 'webview', 'command']) {
              failureStage = stage;
              await assert.rejects(extension.activate(context), error => error === activationFailure);
              await assert.rejects(canvas.host.status(), /Host 已关闭/);
              await new Promise(resolve => reservation.listen(port, '127.0.0.1', resolve));
              await new Promise(resolve => reservation.close(resolve));
              await extension.deactivate();
              for (const resource of context.subscriptions.splice(0)) resource.dispose();
              registrations.length = 0;
            }
            failureStage = undefined;
            await extension.activate(context);
            assert.deepEqual(registrations, ['toolkit.canvasPreview','toolkit.mcpLog','toolkit.showLogs','toolkit.openSettings','toolkit.openDocumentation']);
            const canvasDocument = canvas.openCustomDocument(uri(${JSON.stringify(join(workspaceFullPath, 'unavailable.canvas'))}));
            const panel = { webview: {} };
            const previewRequest = { sourceEntityId: 'a'.repeat(32), viewport: { width: 2, height: 3 }, fonts: [{ name: '固定字体', revision: 'fixture-font-v1', bytes: new Uint8Array([1, 2, 3]) }], timeSeconds: 1.25 };
            const originalRequest = structuredClone(previewRequest);
            const signal = new AbortController().signal;
            const originalFiles = await readdir(${JSON.stringify(workspaceFullPath)}, { recursive: true });
            await assert.rejects(canvas.openPanel(previewRequest, panel, signal), error => error instanceof Error && error.message === 'TODO: 同一 Host 的 Canvas 面板展示、刷新交付与关闭释放接入');
            assert.deepEqual(previewRequest, originalRequest);
            assert.deepEqual(panel, { webview: {} });
            assert.equal(signal.aborted, false);
            assert.deepEqual(await readdir(${JSON.stringify(workspaceFullPath)}, { recursive: true }), originalFiles);
            assert.equal(await readFile(${JSON.stringify(join(workspaceFullPath, 'setting.json'))}, 'utf8'), ${JSON.stringify(settings)});
            await canvas.resolveCustomEditor(canvasDocument, panel);
            const window = new Window();
            try {
              window.document.write(panel.webview.html);
              assert.equal(window.document.querySelector('p').textContent, 'Canvas 预览未接入：等待同一 Host 交付 PNG 和 HTML/XML 文本。');
              assert.equal(window.document.querySelector('img'), null);
              assert.equal(window.document.querySelector('script'), null);
            } finally {
              await window.happyDOM.close();
              canvasDocument.dispose();
            }
            assert.equal((await canvas.host.status()).settings.name, '扩展验证');
            sidebar.resolveWebviewView({ webview: { cspSource: 'test:', asWebviewUri(value) { return value; }, onDidReceiveMessage(callback) { receive = callback; return disposable; }, async postMessage(message) { if (message.kind === 'state') globalThis.configuredState = message; return true; } } });
            receive({ action: 'configure', target: 'gemini' });
            const deadline = Date.now() + 5000;
            while (!globalThis.configuredState?.payload.error) { if (Date.now() > deadline) throw new Error('配置失败状态未交付'); await new Promise(resolve => setTimeout(resolve, 10)); }
            assert.match(globalThis.configuredState.payload.error, /TODO: Gemini 正式 CLI/);
            assert.deepEqual(await readdir(${JSON.stringify(workspaceFullPath)}, { recursive: true }), originalFiles);
            receive({ action: 'configure', target: 'trae' });
            while (globalThis.configuredState.payload.error) { if (Date.now() > deadline) throw new Error('配置未完成'); await new Promise(resolve => setTimeout(resolve, 10)); }
            assert.deepEqual(JSON.parse(await readFile(${JSON.stringify(join(workspaceFullPath, '.trae/mcp.json'))}, 'utf8')), { mcpServers: { mechtoolkit: { type: 'http', url: 'http://127.0.0.1:' + port + '/mcp' } } });
            assert.equal(await readFile(${JSON.stringify(join(workspaceFullPath, '.trae/skills/mechtoolkit/SKILL.md'))}, 'utf8'), ${JSON.stringify(skill)});
          } finally {
            await extension.deactivate();
            await assert.rejects(canvas.host.status(), /Host 已关闭/);
            for (const resource of context.subscriptions) resource.dispose();
          }
        `
        await promisify(execFile)(process.execPath, ['--input-type=module', '--eval', script], {
          cwd: process.cwd(),
          timeout: 10000,
          windowsHide: true,
        })
      } finally {
        let writtenBytes: number = 0
        let maximumBytes: number = 0
        let writtenFileCount: number = 0
        for (const fullPath of writtenPaths) {
          const entry: Awaited<ReturnType<typeof stat>> | null = await stat(fullPath).catch(
            (error: unknown): null => {
              if (error instanceof Error && 'code' in error && error.code === 'ENOENT') return null
              throw error
            },
          )
          if (entry !== null) {
            const size: number = entry.size
            writtenFileCount += 1
            writtenBytes += size
            maximumBytes = Math.max(maximumBytes, size)
          }
        }
        await rm(workspaceFullPath, { recursive: true })
        process.stdout.write(
          `扩展产物测试实际写入：${String(writtenFileCount)} 文件，累计 ${String(writtenBytes)} 字节，最大 ${String(maximumBytes)} 字节\n`,
        )
      }
    })
  }
}
