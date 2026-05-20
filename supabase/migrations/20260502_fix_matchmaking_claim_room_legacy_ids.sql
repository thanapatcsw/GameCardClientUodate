create or replace function public.claim_matchmaking_room(
    p_player_id text,
    p_target_player_count integer,
    p_search_request_id text,
    p_scene_name text default 'SampleScene',
    p_display_name text default null,
    p_stale_timeout_seconds integer default 300
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
    v_now timestamptz := timezone('utc', now());
    v_stale_timeout_seconds integer := greatest(30, least(coalesce(p_stale_timeout_seconds, 300), 3600));
    v_cutoff timestamptz := v_now - make_interval(secs => v_stale_timeout_seconds);
    v_reuse_cutoff timestamptz := v_now - interval '5 minutes';
    v_waiting_row public.matchmaking_queue%rowtype;
    v_existing_match public.matchmaking_queue%rowtype;
    v_selected_ids text[];
    v_players text[];
    v_waiting_players text[];
    v_room_code text;
    v_room_id bigint;
    v_current_player_selected boolean := false;
    v_host_name text := coalesce(nullif(btrim(p_display_name), ''), p_player_id);
    v_scene_name text := coalesce(nullif(btrim(p_scene_name), ''), 'SampleScene');
begin
    if p_player_id is null or btrim(p_player_id) = '' then
        return jsonb_build_object('status', 'error', 'message', 'playerId is required.');
    end if;

    if p_target_player_count not in (2, 3, 4) then
        return jsonb_build_object('status', 'error', 'message', 'targetPlayerCount must be 2, 3, or 4.');
    end if;

    if p_search_request_id is null or btrim(p_search_request_id) = '' then
        return jsonb_build_object('status', 'error', 'message', 'searchRequestId is required.');
    end if;

    perform pg_advisory_xact_lock(hashtext('matchmaking:' || p_target_player_count::text));

    update public.matchmaking_queue
    set status = 'cancelled',
        room_code = null,
        room_id = null,
        matched_at = null
    where status = 'waiting'
      and created_at < v_cutoff;

    with duplicate_waiting_rows as (
        select id
        from (
            select id,
                   row_number() over (partition by player_id order by created_at desc, id desc) as row_num
            from public.matchmaking_queue
            where status = 'waiting'
              and created_at >= v_cutoff
        ) ranked_waiting
        where row_num > 1
    )
    update public.matchmaking_queue
    set status = 'cancelled',
        room_code = null,
        room_id = null,
        matched_at = null
    where id in (select id from duplicate_waiting_rows);

    update public.matchmaking_queue
    set status = 'cancelled',
        room_code = null,
        room_id = null,
        matched_at = null
    where player_id = p_player_id
      and status = 'waiting'
      and (target_player_count <> p_target_player_count or search_request_id <> p_search_request_id);

    select *
    into v_existing_match
    from public.matchmaking_queue
    where player_id = p_player_id
      and status = 'matched'
      and target_player_count = p_target_player_count
      and search_request_id = p_search_request_id
      and matched_at >= v_reuse_cutoff
    order by matched_at desc, created_at desc
    limit 1;

    if found and v_existing_match.room_code is not null then
        select array_agg(player_id order by created_at, id)
        into v_players
        from public.matchmaking_queue
        where room_code = v_existing_match.room_code
          and room_id = v_existing_match.room_id
          and status = 'matched';

        return jsonb_build_object(
            'status', 'matched',
            'playerId', p_player_id,
            'targetPlayerCount', p_target_player_count,
            'roomCode', v_existing_match.room_code,
            'roomId', coalesce(v_existing_match.room_id::text, ''),
            'players', coalesce(to_jsonb(v_players), '[]'::jsonb),
            'searchRequestId', p_search_request_id
        );
    end if;

    select *
    into v_waiting_row
    from public.matchmaking_queue
    where player_id = p_player_id
      and status = 'waiting'
      and target_player_count = p_target_player_count
      and search_request_id = p_search_request_id
    order by created_at desc, id desc
    limit 1
    for update;

    if not found then
        insert into public.matchmaking_queue (
            player_id,
            status,
            room_code,
            room_id,
            target_player_count,
            search_request_id,
            created_at,
            matched_at
        )
        values (
            p_player_id,
            'waiting',
            null,
            null,
            p_target_player_count,
            p_search_request_id,
            v_now,
            null
        )
        returning * into v_waiting_row;
    end if;

    update public.matchmaking_queue
    set status = 'cancelled',
        room_code = null,
        room_id = null,
        matched_at = null
    where player_id = p_player_id
      and status = 'waiting'
      and target_player_count = p_target_player_count
      and search_request_id = p_search_request_id
      and id::text <> v_waiting_row.id::text;

    with current_player_row as (
        select id, id::text as id_text, player_id, created_at
        from public.matchmaking_queue
        where id = v_waiting_row.id
    ),
    distinct_other_rows as (
        select id, id::text as id_text, player_id, created_at
        from (
            select id, player_id, created_at,
                   row_number() over (partition by player_id order by created_at desc, id desc) as row_num
            from public.matchmaking_queue
            where status = 'waiting'
              and target_player_count = p_target_player_count
              and created_at >= v_cutoff
              and id <> v_waiting_row.id
              and player_id <> p_player_id
        ) ranked_waiting
        where row_num = 1
        order by created_at asc, id asc
        limit greatest(p_target_player_count - 1, 0)
    ),
    candidate_rows as (
        select * from current_player_row
        union all
        select * from distinct_other_rows
    )
    select
        array_agg(id_text order by created_at asc, id asc),
        array_agg(player_id order by created_at asc, id asc),
        bool_or(player_id = p_player_id)
    into
        v_selected_ids,
        v_players,
        v_current_player_selected
    from candidate_rows;

    if coalesce(array_length(v_selected_ids, 1), 0) < p_target_player_count then
        select array_agg(player_id order by created_at asc, id asc)
        into v_waiting_players
        from (
            select id, player_id, created_at
            from (
                select id, player_id, created_at,
                       row_number() over (partition by player_id order by created_at desc, id desc) as row_num
                from public.matchmaking_queue
                where status = 'waiting'
                  and target_player_count = p_target_player_count
                  and created_at >= v_cutoff
            ) ranked_waiting
            where row_num = 1
            order by created_at asc, id asc
        ) waiting_snapshot;

        return jsonb_build_object(
            'status', 'waiting',
            'playerId', p_player_id,
            'targetPlayerCount', p_target_player_count,
            'players', coalesce(to_jsonb(v_waiting_players), '[]'::jsonb),
            'message', format(
                'Waiting for %s of %s players',
                coalesce(array_length(v_waiting_players, 1), 0),
                p_target_player_count
            ),
            'searchRequestId', p_search_request_id
        );
    end if;

    loop
        v_room_code := 'ROOM-' || upper(substr(md5(
            p_player_id || ':' || p_search_request_id || ':' || clock_timestamp()::text || ':' || random()::text
        ), 1, 6));
        exit when not exists (
            select 1
            from public.rooms
            where room_code = v_room_code
        );
    end loop;

    insert into public.rooms (
        room_code,
        session_name,
        host_name,
        player_count,
        status,
        created_at
    )
    values (
        v_room_code,
        v_scene_name,
        v_host_name,
        p_target_player_count,
        'waiting',
        v_now
    )
    returning id into v_room_id;

    update public.matchmaking_queue
    set status = 'matched',
        room_code = v_room_code,
        room_id = v_room_id,
        matched_at = v_now
    where id::text = any(v_selected_ids);

    if v_current_player_selected then
        return jsonb_build_object(
            'status', 'matched',
            'playerId', p_player_id,
            'targetPlayerCount', p_target_player_count,
            'roomCode', v_room_code,
            'roomId', coalesce(v_room_id::text, ''),
            'players', coalesce(to_jsonb(v_players), '[]'::jsonb),
            'searchRequestId', p_search_request_id
        );
    end if;

    return jsonb_build_object(
        'status', 'waiting',
        'playerId', p_player_id,
        'targetPlayerCount', p_target_player_count,
        'players', coalesce(to_jsonb(v_players), '[]'::jsonb),
        'searchRequestId', p_search_request_id
    );
end;
$$;
