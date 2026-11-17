import { heatmapView, hourLabel, mondayOf, percent } from './weekly';

describe('weekly helpers', () => {
  it('finds the Monday of a week', () => {
    expect(mondayOf('2026-11-17')).toBe('2026-11-16'); // Tuesday
    expect(mondayOf('2026-11-22')).toBe('2026-11-16'); // Sunday
    expect(mondayOf('2026-11-16')).toBe('2026-11-16');
    expect(mondayOf('2026-03-01')).toBe('2026-02-23'); // across a month end
  });

  it('shapes the heatmap with a minimum 7-19 window and widens it for data', () => {
    const grid = Array.from({ length: 7 }, () => new Array(24).fill(0));
    grid[0][9] = 4;
    grid[4][21] = 1;
    const v = heatmapView(grid);
    expect(v.hours[0]).toBe(7);
    expect(v.hours[v.hours.length - 1]).toBe(21);
    expect(v.max).toBe(4);
    expect(v.rows[0].cells.find((c) => c.hour === 9)!.v).toBe(1);
    expect(v.rows[4].cells.find((c) => c.hour === 21)!.v).toBe(0.25);
    expect(v.rows[1].cells[0].v).toBe(0);
  });

  it('formats hours and percentages', () => {
    expect(hourLabel(9)).toBe('9a');
    expect(hourLabel(12)).toBe('12p');
    expect(hourLabel(15)).toBe('3p');
    expect(percent(1, 3)).toBe(33);
    expect(percent(1, 0)).toBe(0);
  });
});
