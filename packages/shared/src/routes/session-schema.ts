import { z } from 'zod';

export const GpsPointInputSchema = z.object({
  lat: z.number().min(-90).max(90),
  lng: z.number().min(-180).max(180),
  accuracyM: z.number().positive().optional(),
  speedMps: z.number().nonnegative().optional(),
  recordedAt: z.string().datetime(),
});

export type GpsPointInput = z.infer<typeof GpsPointInputSchema>;

export const BeginRouteSessionSchema = z.object({
  worldId: z.string().uuid(),
  empireId: z.string().uuid(),
  originSettlementId: z.string().uuid().optional(),
  targetSettlementId: z.string().uuid().optional(),
  lat: z.number().min(-90).max(90),
  lng: z.number().min(-180).max(180),
});

export type BeginRouteSession = z.infer<typeof BeginRouteSessionSchema>;

export const AppendRoutePointsSchema = z.object({
  points: z.array(GpsPointInputSchema).min(1).max(20),
});

export type AppendRoutePoints = z.infer<typeof AppendRoutePointsSchema>;

export const EndRouteSessionSchema = z.object({
  lat: z.number().min(-90).max(90).optional(),
  lng: z.number().min(-180).max(180).optional(),
});

export type EndRouteSession = z.infer<typeof EndRouteSessionSchema>;

export const RouteSessionStatusSchema = z.enum([
  'active',
  'completing',
  'completed',
  'abandoned',
  'invalid',
]);

export type RouteSessionStatus = z.infer<typeof RouteSessionStatusSchema>;
