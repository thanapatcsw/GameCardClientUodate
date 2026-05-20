create extension if not exists pgcrypto;

create table if not exists public.matchmaking_queue (
    id uuid primary key default gen_random_uuid(),
    player_id text not null,
    status text not null default 'waiting',
    room_code text null,
    room_id bigint null references public.rooms (id) on delete set null,
    target_player_count integer not null default 2,
    search_request_id text not null,
    created_at timestamptz not null default timezone('utc', now()),
    matched_at timestamptz null,
    constraint matchmaking_queue_target_player_count_check
        check (target_player_count in (2, 3, 4))
);

create index if not exists idx_matchmaking_queue_status_target_created_at
    on public.matchmaking_queue (status, target_player_count, created_at);

create index if not exists idx_matchmaking_queue_player_search
    on public.matchmaking_queue (player_id, search_request_id);

create unique index if not exists uq_matchmaking_queue_waiting_player
    on public.matchmaking_queue (player_id)
    where status = 'waiting';

alter table public.matchmaking_queue enable row level security;

drop policy if exists "matchmaking_queue_select_all" on public.matchmaking_queue;
drop policy if exists "matchmaking_queue_insert_all" on public.matchmaking_queue;
drop policy if exists "matchmaking_queue_update_all" on public.matchmaking_queue;

create or replace function public.cancel_matchmaking_search(
    p_player_id text,
    p_search_request_id text default null
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
    v_cancelled_count integer := 0;
begin
    if p_player_id is null or btrim(p_player_id) = '' then
        return jsonb_build_object('status', 'error', 'message', 'playerId is required.');
    end if;

    update public.matchmaking_queue
    set status = 'cancelled',
        room_code = null,
        room_id = null,
        matched_at = null
    where player_id = p_player_id
      and status = 'waiting'
      and (p_search_request_id is null or search_request_id = p_search_request_id);

    get diagnostics v_cancelled_count = row_count;

    return jsonb_build_object(
        'status', 'cancelled',
        'playerId', p_player_id,
        'cancelledCount', v_cancelled_count
    );
end;
$$;
