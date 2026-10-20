import assert from 'node:assert/strict'
import { mkdirSync, mkdtempSync, rmSync } from 'node:fs'
import { resolve } from 'node:path'
import { pathToFileURL } from 'node:url'
import { spawn } from 'node:child_process'
import type { ChildProcess } from 'node:child_process'
import { Option } from 'effect'
import { afterEach, test } from 'vitest'
import { Git } from '../../../tools/cli/Git.ts'
import { RequirementsReviewStore } from '../../../tools/cli/RequirementsReviewStore.ts'
import type {
  ReviewPublication,
  ReviewSnapshot,
  ReviewTicket,
} from '../../../tools/cli/RequirementsReviewStore.ts'

export class RequirementsReviewStoreTest {
  private static readonly projects: string[] = []
  private static readonly first: string = '638c682e-d34a-4136-8e59-44ec8e540131'
  private static readonly second: string = 'c20c846a-557e-4fe7-8ed0-ba72f195689e'
  private static readonly snapshot: ReviewSnapshot = {
    head: 'head',
    index: 'index',
    tree: 'tree',
    parents: ['head'],
  }

  private constructor() {}

  private static project(): string {
    mkdirSync(resolve('_build/requirements/tests'), { recursive: true })
    const rootFullPath: string = mkdtempSync(resolve('_build/requirements/tests/tickets-'))
    RequirementsReviewStoreTest.projects.push(rootFullPath)
    Git.run(rootFullPath, ['init', '--initial-branch=main'])
    return rootFullPath
  }

  private static input(id: string): Omit<ReviewTicket, 'token'> {
    return {
      id,
      snapshot: RequirementsReviewStoreTest.snapshot,
      fingerprint: 'content',
      criteria: 'criteria',
      coverage: 'a'.repeat(64),
      subjects: [],
    }
  }

  private static publication(ticket: ReviewTicket): ReviewPublication {
    return { ticket, verdict: '{"axes":{}}', recordedAt: '2026-10-19T00:00:00.000Z' }
  }

  static {
    afterEach((): void => {
      for (const rootFullPath of RequirementsReviewStoreTest.projects.splice(0))
        rmSync(rootFullPath, { recursive: true })
    })

    test('随机令牌绑定条目，重新领取替代旧令牌，仓库隔离', (): void => {
      const first: RequirementsReviewStore = new RequirementsReviewStore(
        RequirementsReviewStoreTest.project(),
      )
      const second: RequirementsReviewStore = new RequirementsReviewStore(
        RequirementsReviewStoreTest.project(),
      )
      try {
        const ticket: ReviewTicket = Option.getOrThrow(
          Option.fromNullishOr(
            first.issue([RequirementsReviewStoreTest.input(RequirementsReviewStoreTest.first)])[0],
          ),
        )
        assert.equal(ticket.token.length, 64)
        assert.throws((): ReviewTicket => second.ticket(ticket.id, ticket.token))
        const replacement: ReviewTicket = Option.getOrThrow(
          Option.fromNullishOr(first.issue([RequirementsReviewStoreTest.input(ticket.id)])[0]),
        )
        assert.notEqual(replacement.token, ticket.token)
        assert.throws((): ReviewTicket => first.ticket(ticket.id, ticket.token))
        assert.deepEqual(first.ticket(replacement.id, replacement.token), replacement)
        assert.throws((): ReviewTicket =>
          first.ticket(RequirementsReviewStoreTest.second, replacement.token),
        )
      } finally {
        first.close()
        second.close()
      }
    })

    test('整批任一凭据失败不消费其他令牌，重复登记拒绝', (): void => {
      const store: RequirementsReviewStore = new RequirementsReviewStore(
        RequirementsReviewStoreTest.project(),
      )
      try {
        const tickets: readonly ReviewTicket[] = store.issue([
          RequirementsReviewStoreTest.input(RequirementsReviewStoreTest.first),
          RequirementsReviewStoreTest.input(RequirementsReviewStoreTest.second),
        ])
        const first: ReviewPublication = RequirementsReviewStoreTest.publication(
          Option.getOrThrow(Option.fromNullishOr(tickets[0])),
        )
        const second: ReviewPublication = RequirementsReviewStoreTest.publication(
          Option.getOrThrow(Option.fromNullishOr(tickets[1])),
        )
        assert.throws((): void => {
          store.record(
            [first, { ...second, ticket: { ...second.ticket, token: '0'.repeat(64) } }],
            RequirementsReviewStoreTest.snapshot,
          )
        })
        assert.deepEqual(store.pending(), [])
        assert.deepEqual(store.ticket(first.ticket.id, first.ticket.token), first.ticket)
        assert.throws((): void => {
          store.record([first, first], RequirementsReviewStoreTest.snapshot)
        }, /重复/)
        store.record([first, second], RequirementsReviewStoreTest.snapshot)
        assert.equal(store.pending().length, 2)
        assert.throws((): void => {
          store.record([first], RequirementsReviewStoreTest.snapshot)
        })
        store.published(first)
        assert.deepEqual(store.pending(), [second])
      } finally {
        store.close()
      }
    })

    test('HEAD、index 和合并父节点变化拒绝登记且保留令牌', (): void => {
      const store: RequirementsReviewStore = new RequirementsReviewStore(
        RequirementsReviewStoreTest.project(),
      )
      try {
        const ticket: ReviewTicket = Option.getOrThrow(
          Option.fromNullishOr(
            store.issue([RequirementsReviewStoreTest.input(RequirementsReviewStoreTest.first)])[0],
          ),
        )
        for (const snapshot of [
          { ...ticket.snapshot, head: 'changed' },
          { ...ticket.snapshot, index: 'changed' },
          { ...ticket.snapshot, parents: ['other'] },
        ])
          assert.throws((): void => {
            store.record([RequirementsReviewStoreTest.publication(ticket)], snapshot)
          }, /上下文已变化/)
        assert.deepEqual(store.ticket(ticket.id, ticket.token), ticket)
      } finally {
        store.close()
      }
    })

    test('两个连接争用同一令牌只有一次成功', (): void => {
      const rootFullPath: string = RequirementsReviewStoreTest.project()
      const first: RequirementsReviewStore = new RequirementsReviewStore(rootFullPath)
      const second: RequirementsReviewStore = new RequirementsReviewStore(rootFullPath)
      try {
        const ticket: ReviewTicket = Option.getOrThrow(
          Option.fromNullishOr(
            first.issue([RequirementsReviewStoreTest.input(RequirementsReviewStoreTest.first)])[0],
          ),
        )
        const publication: ReviewPublication = RequirementsReviewStoreTest.publication(ticket)
        first.record([publication], ticket.snapshot)
        assert.throws((): void => {
          second.record([publication], ticket.snapshot)
        })
        assert.deepEqual(second.pending(), [publication])
      } finally {
        first.close()
        second.close()
      }
    })

    test('发布失败保留待发布意见，旧发布确认不能删除重新登记的意见', (): void => {
      const rootFullPath: string = RequirementsReviewStoreTest.project()
      const store: RequirementsReviewStore = new RequirementsReviewStore(rootFullPath)
      try {
        const first: ReviewTicket = Option.getOrThrow(
          Option.fromNullishOr(
            store.issue([RequirementsReviewStoreTest.input(RequirementsReviewStoreTest.first)])[0],
          ),
        )
        const original: ReviewPublication = RequirementsReviewStoreTest.publication(first)
        store.record([original], first.snapshot)
        assert.throws((): void => {
          store.publish((): boolean => {
            throw new Error('文件发布失败')
          })
        }, /文件发布失败/)
        assert.deepEqual(store.pending(), [original])
        const replacement: ReviewTicket = Option.getOrThrow(
          Option.fromNullishOr(store.issue([RequirementsReviewStoreTest.input(first.id)])[0]),
        )
        const changed: ReviewPublication = RequirementsReviewStoreTest.publication(replacement)
        store.record([changed], replacement.snapshot)
        store.published(original)
        assert.deepEqual(store.pending(), [changed])
        store.publish((): boolean => false)
        assert.deepEqual(store.pending(), [changed])
        const emitted: ReviewPublication[] = []
        store.publish((publication: ReviewPublication): boolean => {
          emitted.push(publication)
          return true
        })
        assert.deepEqual(emitted, [changed])
        assert.deepEqual(store.pending(), [])
      } finally {
        store.close()
      }
    })

    test('图中删除的条目移除私有凭据，仍在图中的待发布意见保留', (): void => {
      const store: RequirementsReviewStore = new RequirementsReviewStore(
        RequirementsReviewStoreTest.project(),
      )
      try {
        const tickets: readonly ReviewTicket[] = store.issue([
          RequirementsReviewStoreTest.input(RequirementsReviewStoreTest.first),
          RequirementsReviewStoreTest.input(RequirementsReviewStoreTest.second),
        ])
        const first: ReviewTicket = Option.getOrThrow(Option.fromNullishOr(tickets[0]))
        const second: ReviewTicket = Option.getOrThrow(Option.fromNullishOr(tickets[1]))
        const publication: ReviewPublication = RequirementsReviewStoreTest.publication(first)
        store.record([publication], first.snapshot)
        store.retain([first.id])
        assert.deepEqual(store.pending(), [publication])
        assert.throws((): ReviewTicket => store.ticket(second.id, second.token))
        store.retain([])
        assert.deepEqual(store.pending(), [])
      } finally {
        store.close()
      }
    })

    test('两个真实进程同时消费同一令牌只有一个事务成功', async (): Promise<void> => {
      const rootFullPath: string = RequirementsReviewStoreTest.project()
      const store: RequirementsReviewStore = new RequirementsReviewStore(rootFullPath)
      const ticket: ReviewTicket = Option.getOrThrow(
        Option.fromNullishOr(
          store.issue([RequirementsReviewStoreTest.input(RequirementsReviewStoreTest.first)])[0],
        ),
      )
      const children: ChildProcess[] = []
      try {
        const source: string = [
          'const { RequirementsReviewStore } = await import(process.argv[1]);',
          'const store = new RequirementsReviewStore(process.argv[2]);',
          'process.send("ready");',
          'process.once("message", () => {',
          'try { const publication = JSON.parse(process.argv[3]); store.record([publication], publication.ticket.snapshot); process.exitCode = 0; }',
          'catch { process.exitCode = 1; }',
          'finally { store.close(); process.disconnect(); }',
          '});',
        ].join('\n')
        const publication: ReviewPublication = RequirementsReviewStoreTest.publication(ticket)
        const completed: Promise<number | null>[] = []
        await Promise.all(
          [0, 1].map(
            (): Promise<void> =>
              new Promise<void>((ready: () => void, failed: (error: Error) => void): void => {
                const child: ChildProcess = spawn(
                  process.execPath,
                  [
                    '--import',
                    pathToFileURL(resolve('node_modules/tsx/dist/loader.mjs')).href,
                    '--input-type=module',
                    '-e',
                    source,
                    pathToFileURL(resolve('tools/cli/RequirementsReviewStore.ts')).href,
                    rootFullPath,
                    JSON.stringify(publication),
                  ],
                  { windowsHide: true, stdio: ['ignore', 'ignore', 'ignore', 'ipc'] },
                )
                children.push(child)
                completed.push(
                  new Promise<number | null>((exited: (code: number | null) => void): void => {
                    child.once('exit', exited)
                  }),
                )
                child.once('error', failed)
                child.once('message', (): void => {
                  ready()
                })
              }),
          ),
        )
        for (const child of children) child.send('consume')
        const results: readonly (number | null)[] = await Promise.all(completed)
        assert.deepEqual(
          [...results].sort((left: number | null, right: number | null): number =>
            String(left).localeCompare(String(right), 'en'),
          ),
          [0, 1],
        )
        assert.deepEqual(store.pending(), [publication])
      } finally {
        for (const child of children) if (child.exitCode === null) child.kill()
        store.close()
      }
    })

    test('同一 common dir 的真实 worktree 不共享领取或 rebase 输入', (): void => {
      const rootFullPath: string = RequirementsReviewStoreTest.project()
      Git.run(rootFullPath, [
        '-c',
        'user.name=Fixture',
        '-c',
        'user.email=fixture@example.invalid',
        '-c',
        'commit.gpgsign=false',
        'commit',
        '--allow-empty',
        '-m',
        'test: worktree input',
      ])
      const worktreeFullPath: string = resolve(rootFullPath, 'linked')
      Git.run(rootFullPath, ['worktree', 'add', '--detach', worktreeFullPath, 'HEAD'])
      const first: RequirementsReviewStore = new RequirementsReviewStore(rootFullPath)
      const second: RequirementsReviewStore = new RequirementsReviewStore(worktreeFullPath)
      try {
        assert.equal(
          Git.run(rootFullPath, ['rev-parse', '--path-format=absolute', '--git-common-dir']),
          Git.run(worktreeFullPath, ['rev-parse', '--path-format=absolute', '--git-common-dir']),
        )
        const ticket: ReviewTicket = Option.getOrThrow(
          Option.fromNullishOr(
            first.issue([RequirementsReviewStoreTest.input(RequirementsReviewStoreTest.first)])[0],
          ),
        )
        assert.throws((): ReviewTicket => second.ticket(ticket.id, ticket.token))
        first.startRewrite('original')
        assert.equal(Option.isNone(second.rewriteHead()), true)
        assert.equal(Option.getOrThrow(first.rewriteHead()), 'original')
        first.finishRewrite('different')
        assert.equal(Option.getOrThrow(first.rewriteHead()), 'original')
        first.finishRewrite('original')
        assert.equal(Option.isNone(first.rewriteHead()), true)
      } finally {
        first.close()
        second.close()
      }
    })
  }
}
