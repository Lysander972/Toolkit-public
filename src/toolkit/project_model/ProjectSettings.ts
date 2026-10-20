import Hjson from 'hjson'
import { z } from 'zod'

/** @anchor project-settings */
export interface ProjectSettings {
  readonly guid: string
  readonly name: string
  readonly mainMap: string
}

/** @anchor project-settings */
export class ProjectSettingsParser {
  private static readonly schema: z.ZodType<ProjectSettings> = z.object({
    guid: z.string().min(1),
    name: z.string(),
    mainMap: z.string(),
  })

  private constructor() {}

  /** @anchor project-settings */
  public static parse(source: string): ProjectSettings {
    return ProjectSettingsParser.schema.parse(Hjson.parse(source))
  }
}
