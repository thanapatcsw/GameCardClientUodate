-- Migration to remove 'coins' premium currency from player_profiles table

ALTER TABLE public.player_profiles
DROP COLUMN IF EXISTS coins;
