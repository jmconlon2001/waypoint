import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', loadComponent: () => import('./pages/today.page').then((m) => m.TodayPage), title: 'Today · Waypoint' },
  { path: 'tasks', loadComponent: () => import('./pages/tasks.page').then((m) => m.TasksPage), title: 'Tasks · Waypoint' },
  { path: 'review', loadComponent: () => import('./pages/review.page').then((m) => m.ReviewPage), title: 'Review · Waypoint' },
  { path: 'settings', loadComponent: () => import('./pages/settings.page').then((m) => m.SettingsPage), title: 'Projects & settings · Waypoint' },
  { path: '**', redirectTo: '' },
];
