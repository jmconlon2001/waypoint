import { Component, ElementRef, effect, inject, input, OnDestroy, viewChild } from '@angular/core';
import {
  ArcElement, BarController, BarElement, CategoryScale, Chart, ChartConfiguration, DoughnutController, Legend,
  LinearScale, LineController, LineElement, PointElement, Tooltip,
} from 'chart.js';
import { ThemeService } from '../core/theme.service';

Chart.register(BarController, BarElement, LineController, LineElement, PointElement, DoughnutController, ArcElement,
  CategoryScale, LinearScale, Tooltip, Legend);

/** Fixed series colors. The gradient stays reserved for the running timer. */
export const KIND_COLORS = {
  meetings: '#0891b2',
  focus: '#2563eb',
  reactive: '#10b981',
  untracked: '#94a3b8',
  unlogged: 'rgba(148, 163, 184, .35)',
  interruptions: '#f59e0b',
} as const;

/** Thin wrapper around a Chart.js canvas: re-renders when the config or the theme changes. */
@Component({
  selector: 'app-chart',
  template: `<div class="chart-box" [style.height.px]="height()"><canvas #canvas [attr.aria-label]="label()" role="img"></canvas></div>`,
})
export class ChartView implements OnDestroy {
  readonly config = input.required<ChartConfiguration>();
  readonly height = input(260);
  readonly label = input('Chart');
  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private readonly theme = inject(ThemeService);
  private chart: Chart | null = null;

  constructor() {
    effect(() => {
      const config = this.config();
      this.theme.theme(); // re-render with the new theme colors
      const el = this.canvas().nativeElement;
      this.chart?.destroy();
      const css = getComputedStyle(document.documentElement);
      Chart.defaults.color = css.getPropertyValue('--sm-muted').trim() || '#64748b';
      Chart.defaults.borderColor = css.getPropertyValue('--sm-border').trim() || '#e5e8ef';
      Chart.defaults.font.family = css.getPropertyValue('--sm-font').trim() || 'Inter, sans-serif';
      this.chart = new Chart(el, {
        ...config,
        options: { responsive: true, maintainAspectRatio: false, animation: { duration: 250 }, ...config.options },
      });
    });
  }

  ngOnDestroy(): void {
    this.chart?.destroy();
  }
}
