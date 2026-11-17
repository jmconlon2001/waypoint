import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from './api.service';
import { Project, Settings } from './models';
import { DEFAULT_TZ, localDate } from './time';

/** App-wide settings and projects, loaded once and refreshed after edits. */
@Injectable({ providedIn: 'root' })
export class SettingsStore {
  private readonly api = inject(ApiService);

  readonly settings = signal<Settings | null>(null);
  readonly allProjects = signal<Project[]>([]);
  readonly projects = computed(() => this.allProjects().filter((p) => p.active));
  readonly tz = computed(() => this.settings()?.timeZone || DEFAULT_TZ);
  readonly palette = computed(() => this.settings()?.projectColors ?? []);

  async load(): Promise<void> {
    const [s, p] = await Promise.all([this.api.getSettings(), this.api.getProjects(true)]);
    this.settings.set(s);
    this.allProjects.set(p);
  }

  async refreshProjects(): Promise<void> {
    this.allProjects.set(await this.api.getProjects(true));
  }

  today(): string {
    return localDate(new Date(), this.tz());
  }

  project(id: number | null | undefined): Project | undefined {
    return id == null ? undefined : this.allProjects().find((p) => p.id === id);
  }
}
