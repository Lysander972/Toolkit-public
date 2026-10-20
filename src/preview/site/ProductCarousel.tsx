import { Component } from 'react'
import type { FocusEvent, ReactNode } from 'react'
import { Option } from 'effect'

interface CarouselSlide {
  readonly title: string
  readonly detail: string
}

interface CarouselState {
  readonly index: number
  readonly hovered: boolean
  readonly focused: boolean
}

/** @anchor preview-site */
export class ProductCarousel extends Component<object, CarouselState> {
  private static readonly slides: readonly CarouselSlide[] = [
    { title: '运行画面一', detail: '成品画面资源待补齐。' },
    { title: '运行画面二', detail: '成品画面资源待补齐。' },
    { title: '运行画面三', detail: '成品画面资源待补齐。' },
  ]
  private interval: Option.Option<ReturnType<typeof setInterval>> = Option.none()
  public override readonly state: CarouselState = { index: 0, hovered: false, focused: false }

  /** @anchor preview-site */
  public override componentDidMount(): void {
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return
    this.interval = Option.some(
      setInterval((): void => {
        if (!this.state.hovered && !this.state.focused) this.next()
      }, 4500),
    )
  }

  /** @anchor preview-site */
  public override componentWillUnmount(): void {
    if (Option.isSome(this.interval)) clearInterval(this.interval.value)
  }

  private next(): void {
    this.setState((state: CarouselState): CarouselState => ({
      ...state,
      index: (state.index + 1) % ProductCarousel.slides.length,
    }))
  }

  private previous(): void {
    this.setState((state: CarouselState): CarouselState => ({
      ...state,
      index: (state.index + ProductCarousel.slides.length - 1) % ProductCarousel.slides.length,
    }))
  }

  private select(index: number): void {
    this.setState({ index })
  }

  /** @anchor preview-site */
  public override render(): ReactNode {
    const slide: CarouselSlide = Option.getOrThrow(
      Option.fromNullishOr(ProductCarousel.slides[this.state.index]),
    )
    return (
      <div
        className="carousel"
        onMouseEnter={(): void => {
          this.setState({ hovered: true })
        }}
        onMouseLeave={(): void => {
          this.setState({ hovered: false })
        }}
        onFocus={(): void => {
          this.setState({ focused: true })
        }}
        onBlur={(event: FocusEvent<HTMLDivElement>): void => {
          if (!event.currentTarget.contains(event.relatedTarget)) this.setState({ focused: false })
        }}
      >
        <div className="carousel-stage" aria-live="polite">
          <div className="carousel-placeholder">
            <span className="carousel-index">
              {String(this.state.index + 1).padStart(2, '0')} /{' '}
              {String(ProductCarousel.slides.length).padStart(2, '0')}
            </span>
            <strong>{slide.title}</strong>
            <span>{slide.detail}</span>
          </div>
        </div>
        <div className="carousel-controls">
          <button
            type="button"
            onClick={(): void => {
              this.previous()
            }}
            aria-label="上一张"
          >
            ←
          </button>
          <div className="carousel-dots" role="tablist" aria-label="成品画面">
            {ProductCarousel.slides.map((item: CarouselSlide, index: number): ReactNode => (
              <button
                key={item.title}
                type="button"
                role="tab"
                aria-label={item.title}
                aria-selected={index === this.state.index}
                className={index === this.state.index ? 'carousel-dot active' : 'carousel-dot'}
                onClick={(): void => {
                  this.select(index)
                }}
              />
            ))}
          </div>
          <button
            type="button"
            onClick={(): void => {
              this.next()
            }}
            aria-label="下一张"
          >
            →
          </button>
        </div>
      </div>
    )
  }
}
