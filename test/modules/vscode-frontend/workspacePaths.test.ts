// 工作区路径规则测试：相对路径纪律（拒绝绝对路径/`\`/`..`/空段）是跨宿主契约。
import { test } from 'vitest'
import assert from 'node:assert/strict'
import path from 'node:path'
import { WorkspacePath } from '../../../src/frontend/host_client/workspacePath.ts'

/** @anchor frontend-workspace-path */
export class WorkspacePathsTest {
  /** @anchor frontend-workspace-path */
  private static acceptsLegalRelative(this: void): void {
    assert.equal(WorkspacePath.requireRelative('机械/1/device.json'), '机械/1/device.json')
    assert.equal(WorkspacePath.requireRelative('.'), '.')
  }

  /** @anchor frontend-workspace-path */
  private static rejectsAbsoluteAndBackslash(this: void): void {
    const absoluteFullPath: string = path.resolve('x', 'y')
    assert.throws((): void => {
      WorkspacePath.requireRelative(absoluteFullPath.replaceAll(path.sep, '/'))
    })
    assert.throws((): void => {
      WorkspacePath.requireRelative(absoluteFullPath)
    })
    assert.throws((): void => {
      WorkspacePath.requireRelative('a\\b')
    })
  }

  /** @anchor frontend-workspace-path */
  private static rejectsDotDotAndEmptySegments(this: void): void {
    assert.throws((): void => {
      WorkspacePath.requireRelative('../x')
    })
    assert.throws((): void => {
      WorkspacePath.requireRelative('a//b')
    })
    assert.throws((): void => {
      WorkspacePath.requireRelative('a/')
    })
  }

  /** @anchor frontend-workspace-path */
  private static resolvesFullWithSystemSeparator(this: void): void {
    const workspaceFullPath: string = path.resolve('project-fixture')
    const joined: string = WorkspacePath.resolveFull('a/b.json', workspaceFullPath)
    assert.equal(joined, path.join(workspaceFullPath, 'a', 'b.json'))
  }

  /** @anchor frontend-workspace-path */
  private static normalizesToRelative(this: void): void {
    const workspaceFullPath: string = path.resolve('project-fixture')
    assert.equal(WorkspacePath.toRelative(workspaceFullPath, workspaceFullPath), '.')
    const rel: string = WorkspacePath.toRelative(
      path.join(workspaceFullPath, 'a', 'b.json'),
      workspaceFullPath,
    )
    assert.equal(rel, 'a/b.json')
    assert.equal(
      WorkspacePath.toRelative(
        path.join(workspaceFullPath, '..assets', 'file.json'),
        workspaceFullPath,
      ),
      '..assets/file.json',
    )
    assert.throws((): void => {
      WorkspacePath.toRelative(path.resolve('other-fixture', 'x.json'), workspaceFullPath)
    })
  }

  static {
    test('workspacePaths:requireRelative 接受合法相对路径', (): void => {
      WorkspacePathsTest.acceptsLegalRelative()
    })
    test('workspacePaths:requireRelative 拒绝绝对路径与反斜杠', (): void => {
      WorkspacePathsTest.rejectsAbsoluteAndBackslash()
    })
    test('workspacePaths:requireRelative 拒绝 .. 与空段', (): void => {
      WorkspacePathsTest.rejectsDotDotAndEmptySegments()
    })
    test('workspacePaths:resolveFull 用系统分隔符拼绝对路径', (): void => {
      WorkspacePathsTest.resolvesFullWithSystemSeparator()
    })
    test('workspacePaths:toRelative 归一化为 / 分隔', (): void => {
      WorkspacePathsTest.normalizesToRelative()
    })
  }
}
