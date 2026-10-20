import { Component } from 'react'
import type { ReactNode } from 'react'

interface MotionTrackProps {
  readonly sectionId: string
  readonly trackId: string
  readonly children: ReactNode
  readonly className: string
}

/** @anchor preview-site */
export class MotionTrack extends Component<MotionTrackProps> {
  private readonly stageRole: string = 'motion-stage'

  /** @anchor preview-motion */
  public override render(): ReactNode {
    return (
      <div
        id={this.props.sectionId}
        className={`motion-track ${this.props.className}`}
        data-motion-track={this.props.trackId}
      >
        <div className={this.stageRole}>{this.props.children}</div>
      </div>
    )
  }
}
