import { DatabaseSync } from 'node:sqlite'
import { randomBytes } from 'node:crypto'
import { z } from 'zod'
import { isDeepStrictEqual } from 'node:util'
import { Option } from 'effect'
import type { StatementSync } from 'node:sqlite'
import { Git } from './Git.ts'

export interface ReviewSnapshot {
  readonly head: string
  readonly index: string
  readonly tree: string
  readonly parents: readonly string[]
}

export interface ReviewTicket {
  readonly id: string
  readonly token: string
  readonly snapshot: ReviewSnapshot
  readonly fingerprint: string
  readonly criteria: string
  readonly coverage: string
  readonly subjects: readonly ReviewSubject[]
}

export interface ReviewSubject {
  readonly id: string
  readonly type: string
  readonly relation: string
  readonly hash: string
}

export interface ReviewPublication {
  readonly ticket: ReviewTicket
  readonly verdict: string
  readonly recordedAt: string
}

interface DatabaseBody {
  readonly body: string
}

interface RewriteRow {
  readonly source_head: string
}

export class RequirementsReviewStore {
  private readonly database: DatabaseSync

  public constructor(repositoryFullPath: string) {
    this.database = new DatabaseSync(
      Git.privatePath(repositoryFullPath, 'requirements-review.sqlite'),
    )
    this.database.exec(`
      PRAGMA busy_timeout = 0;
      CREATE TABLE IF NOT EXISTS tickets (
        id TEXT PRIMARY KEY, token TEXT NOT NULL UNIQUE, body TEXT NOT NULL
      );
      CREATE TABLE IF NOT EXISTS pending (
        id TEXT PRIMARY KEY, body TEXT NOT NULL
      );
      CREATE TABLE IF NOT EXISTS rewrite_input (
        singleton INTEGER PRIMARY KEY CHECK(singleton = 1), source_head TEXT NOT NULL
      );
    `)
  }

  /** @passthrough-exempt 关闭本对象持有的 SQLite 连接。 */
  public close(): void {
    this.database.close()
  }

  public issue(inputs: readonly Omit<ReviewTicket, 'token'>[]): readonly ReviewTicket[] {
    return this.transaction((): readonly ReviewTicket[] => {
      const tickets: ReviewTicket[] = inputs.map(
        (input: Omit<ReviewTicket, 'token'>): ReviewTicket => ({
          ...input,
          token: randomBytes(32).toString('hex'),
        }),
      )
      const statement: StatementSync = this.database.prepare(
        'INSERT INTO tickets (id, token, body) VALUES (?, ?, ?) ON CONFLICT(id) DO UPDATE SET token=excluded.token, body=excluded.body',
      )
      for (const ticket of tickets) statement.run(ticket.id, ticket.token, JSON.stringify(ticket))
      return tickets
    })
  }

  public ticket(id: string, token: string): ReviewTicket {
    const row: DatabaseBody = RequirementsReviewStore.bodySchema.parse(
      this.database.prepare('SELECT body FROM tickets WHERE id = ? AND token = ?').get(id, token),
    )
    return RequirementsReviewStore.ticketSchema.parse(JSON.parse(row.body))
  }

  public record(publications: readonly ReviewPublication[], current: ReviewSnapshot): void {
    this.transaction((): void => {
      const ids: Set<string> = new Set()
      const tokens: Set<string> = new Set()
      for (const publication of publications) {
        const { ticket }: ReviewPublication = publication
        if (ids.has(ticket.id) || tokens.has(ticket.token))
          throw new Error('批量登记包含重复条目或令牌')
        ids.add(ticket.id)
        tokens.add(ticket.token)
        const issued: ReviewTicket = this.ticket(ticket.id, ticket.token)
        if (!isDeepStrictEqual(issued, ticket)) throw new Error('领取凭据已变化')
        if (!isDeepStrictEqual(ticket.snapshot, current))
          throw new Error('受审 HEAD、index 或合并上下文已变化，请重新领取')
      }
      for (const publication of publications) {
        const { ticket }: ReviewPublication = publication
        const consumed: ReturnType<StatementSync['run']> = this.database
          .prepare('DELETE FROM tickets WHERE id = ? AND token = ?')
          .run(ticket.id, ticket.token)
        if (consumed.changes !== 1) throw new Error('令牌已消费')
        this.database
          .prepare(
            'INSERT INTO pending (id, body) VALUES (?, ?) ON CONFLICT(id) DO UPDATE SET body=excluded.body',
          )
          .run(
            ticket.id,
            JSON.stringify(RequirementsReviewStore.publicationSchema.parse(publication)),
          )
      }
    })
  }

  public pending(): readonly ReviewPublication[] {
    return this.database
      .prepare('SELECT body FROM pending ORDER BY id')
      .all()
      .map((row: unknown): ReviewPublication =>
        RequirementsReviewStore.publicationSchema.parse(
          JSON.parse(RequirementsReviewStore.bodySchema.parse(row).body),
        ),
      )
  }

  public published(publication: ReviewPublication): void {
    this.database
      .prepare('DELETE FROM pending WHERE id = ? AND body = ?')
      .run(
        publication.ticket.id,
        JSON.stringify(RequirementsReviewStore.publicationSchema.parse(publication)),
      )
  }

  public publish(operation: (publication: ReviewPublication) => boolean): void {
    this.transaction((): void => {
      for (const publication of this.pending()) {
        if (operation(publication)) this.published(publication)
      }
    })
  }

  public retain(ids: readonly string[]): void {
    this.transaction((): void => {
      const body: string = JSON.stringify(ids)
      this.database
        .prepare('DELETE FROM tickets WHERE id NOT IN (SELECT value FROM json_each(?))')
        .run(body)
      this.database
        .prepare('DELETE FROM pending WHERE id NOT IN (SELECT value FROM json_each(?))')
        .run(body)
    })
  }

  public startRewrite(head: string): void {
    this.database
      .prepare(
        'INSERT INTO rewrite_input (singleton, source_head) VALUES (1, ?) ON CONFLICT(singleton) DO UPDATE SET source_head=excluded.source_head',
      )
      .run(head)
  }

  public rewriteHead(): Option.Option<string> {
    return Option.map(
      Option.fromNullishOr(
        this.database.prepare('SELECT source_head FROM rewrite_input WHERE singleton = 1').get(),
      ),
      (row: unknown): string => RequirementsReviewStore.rewriteSchema.parse(row).source_head,
    )
  }

  public finishRewrite(head: string): void {
    this.database
      .prepare('DELETE FROM rewrite_input WHERE singleton = 1 AND source_head = ?')
      .run(head)
  }

  private transaction<Result>(operation: () => Result): Result {
    this.database.exec('BEGIN IMMEDIATE')
    try {
      const result: Result = operation()
      this.database.exec('COMMIT')
      return result
    } catch (failure: unknown) {
      this.database.exec('ROLLBACK')
      throw failure
    }
  }

  private static readonly bodySchema: z.ZodType<DatabaseBody> = z.object({ body: z.string() })

  private static readonly rewriteSchema: z.ZodType<RewriteRow> = z.object({
    source_head: z.string(),
  })

  public static readonly snapshotSchema: z.ZodType<ReviewSnapshot> = z.object({
    head: z.string(),
    index: z.string(),
    tree: z.string(),
    parents: z.array(z.string()),
  })

  public static readonly ticketSchema: z.ZodType<ReviewTicket> = z.object({
    id: z.uuidv4(),
    token: z.hex().length(64),
    snapshot: RequirementsReviewStore.snapshotSchema,
    fingerprint: z.string().min(1),
    criteria: z.string().min(1),
    coverage: z.hash('sha256'),
    subjects: z.array(
      z.object({
        id: z.uuidv4(),
        type: z.string(),
        relation: z.string(),
        hash: z.hash('sha256'),
      }),
    ),
  })

  public static readonly publicationSchema: z.ZodType<ReviewPublication> = z.object({
    ticket: RequirementsReviewStore.ticketSchema,
    verdict: z.string().min(1),
    recordedAt: z.iso.datetime(),
  })
}
