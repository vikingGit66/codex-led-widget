------------------------------------
-- Codex quota UART dashboard for DaCai DC32480S035.
-- ESP32 sends one ASCII frame:
-- $CQ;PR=87;PU=13;PCT=2h 11m;PRT=06/19 06:37;SR=74;SU=26;SCT=5d 8h;SRT=06/25 10:28;TP=21;SAFE=29;DR=17.8;RISK=1;PLAN=prolite;UP=06/19 02:16;IP=192.168.1.23#
------------------------------------

uart_free_protocol = 1

local SCREEN_ID = 0
local MAX_FRAME_LEN = 360
local BAR_WIDTH = 20

local C = {
    status_text = 20,
    primary_remaining = 21,
    primary_used = 22,
    primary_countdown = 23,
    primary_reset = 24,
    secondary_remaining = 25,
    secondary_used = 26,
    secondary_countdown = 27,
    secondary_reset = 28,
    quota_bar = 29,
    time_bar = 30,
    safe_bar = 31,
    weekly_status = 32,
    weekly_hint = 33,
    plan_text = 34,
    updated_text = 35,
    ip_text = 36,
    daily_rate = 37,
    risk_red = 38,
    risk_yellow = 39,
    risk_green = 40,
    raw_frame = 41
}

local rx_started = 0
local rx_len = 0
local rx = {}

function on_init()
    uart_set_timeout(0, 0)
    boot_ui()
end

function on_systick()
end

function boot_ui()
    ui_text(C.status_text, "等待 ESP32")
    ui_text(C.primary_remaining, "--%")
    ui_text(C.primary_used, "--%")
    ui_text(C.primary_countdown, "--")
    ui_text(C.primary_reset, "--")
    ui_text(C.secondary_remaining, "--%")
    ui_text(C.secondary_used, "--%")
    ui_text(C.secondary_countdown, "--")
    ui_text(C.secondary_reset, "--")
    ui_text(C.quota_bar, make_bar(0))
    ui_text(C.time_bar, make_bar(0))
    ui_text(C.safe_bar, make_bar(0))
    ui_text(C.weekly_status, "未同步")
    ui_text(C.weekly_hint, "局域网数据等待中")
    ui_text(C.plan_text, "--")
    ui_text(C.updated_text, "--")
    ui_text(C.ip_text, "--")
    ui_text(C.daily_rate, "--")
    show_risk(0)
end

function on_uart_recv_data(packet)
    local recv_packet_size = (#(packet))

    for i = 0, recv_packet_size do
        local byte = packet[i]
        if byte ~= nil then
            if byte == 36 then -- "$"
                rx_started = 1
                rx_len = 0
            end

            if rx_started == 1 then
                rx_len = rx_len + 1
                rx[rx_len] = byte

                if byte == 35 then -- "#"
                    local frame = bytes_to_string(rx, rx_len)
                    rx_started = 0
                    rx_len = 0
                    apply_frame(frame)
                elseif rx_len >= MAX_FRAME_LEN then
                    rx_started = 0
                    rx_len = 0
                end
            end
        end
    end
end

function apply_frame(frame)
    if string.sub(frame, 1, 3) ~= "$CQ" then
        return
    end

    ui_text(C.raw_frame, short_text(frame, 40))

    local err = field(frame, "ERR")
    if err ~= "" then
        ui_text(C.status_text, "通信异常")
        ui_text(C.weekly_status, err)
        ui_text(C.weekly_hint, short_text(field(frame, "MSG"), 22))
        ui_text(C.ip_text, field(frame, "IP"))
        show_risk(2)
        return
    end

    if field(frame, "BOOT") ~= "" then
        ui_text(C.status_text, "ESP32 已启动")
        ui_text(C.weekly_status, "等待额度数据")
        ui_text(C.weekly_hint, short_text(field(frame, "MSG"), 22))
        ui_text(C.ip_text, field(frame, "IP"))
        show_risk(0)
        return
    end

    local pr = number_field(frame, "PR", 0)
    local pu = number_field(frame, "PU", 0)
    local sr = number_field(frame, "SR", 0)
    local su = number_field(frame, "SU", 0)
    local tp = number_field(frame, "TP", 0)
    local safe = number_field(frame, "SAFE", 0)
    local risk = number_field(frame, "RISK", 0)
    local r5 = number_field(frame, "R5", 0)
    local r7 = number_field(frame, "R7", 0)

    ui_text(C.status_text, status_text(risk, r5, r7))
    ui_text(C.primary_remaining, pr .. "%")
    ui_text(C.primary_used, pu .. "%")
    ui_text(C.primary_countdown, field_or_dash(frame, "PCT"))
    ui_text(C.primary_reset, field_or_dash(frame, "PRT"))

    ui_text(C.secondary_remaining, sr .. "%")
    ui_text(C.secondary_used, su .. "%")
    ui_text(C.secondary_countdown, field_or_dash(frame, "SCT"))
    ui_text(C.secondary_reset, field_or_dash(frame, "SRT"))

    ui_text(C.quota_bar, make_bar(su))
    ui_text(C.time_bar, make_bar(tp))
    ui_text(C.safe_bar, make_bar(safe))

    ui_text(C.weekly_status, weekly_status_text(r7))
    ui_text(C.weekly_hint, "已用 " .. su .. "% / 安全 " .. safe .. "% / 时间 " .. tp .. "%")
    ui_text(C.plan_text, field_or_dash(frame, "PLAN"))
    ui_text(C.updated_text, field_or_dash(frame, "UP"))
    ui_text(C.ip_text, field_or_dash(frame, "IP"))
    ui_text(C.daily_rate, field_or_dash(frame, "DR") .. "%/天")
    show_risk(risk)
end

function status_text(risk, r5, r7)
    if r5 >= 2 then
        return "5小时额度不足"
    end
    if r7 >= 2 then
        return "7天节奏过快"
    end
    if risk >= 1 then
        return "注意节奏"
    end
    return "额度正常"
end

function weekly_status_text(risk)
    if risk >= 2 then
        return "红色警报"
    end
    if risk == 1 then
        return "接近上限"
    end
    return "节奏稳定"
end

function show_risk(risk)
    ui_text(C.risk_red, "")
    ui_text(C.risk_yellow, "")
    ui_text(C.risk_green, "")

    if risk >= 2 then
        ui_text(C.risk_red, "O")
    elseif risk == 1 then
        ui_text(C.risk_yellow, "O")
    else
        ui_text(C.risk_green, "O")
    end
end

function make_bar(percent)
    local p = clamp(tonumber(percent) or 0, 0, 100)
    local filled = math.floor((p * BAR_WIDTH + 50) / 100)
    local s = "["

    for i = 1, BAR_WIDTH do
        if i <= filled then
            s = s .. "#"
        else
            s = s .. "-"
        end
    end

    return s .. "]"
end

function field_or_dash(frame, key)
    local value = field(frame, key)
    if value == "" then
        return "--"
    end
    return value
end

function field(frame, key)
    local token = ";" .. key .. "="
    local start_pos = string.find(frame, token, 1, true)
    if start_pos == nil then
        return ""
    end

    local value_start = start_pos + string.len(token)
    local value_end = string.find(frame, ";", value_start, true)
    local frame_end = string.find(frame, "#", value_start, true)

    if value_end == nil or (frame_end ~= nil and frame_end < value_end) then
        value_end = frame_end
    end

    if value_end == nil then
        value_end = string.len(frame) + 1
    end

    return string.sub(frame, value_start, value_end - 1)
end

function number_field(frame, key, default_value)
    local raw = field(frame, key)
    if raw == "" then
        return default_value
    end

    local value = tonumber(raw)
    if value == nil then
        return default_value
    end

    return clamp(value, 0, 100)
end

function clamp(value, low, high)
    if value < low then
        return low
    end
    if value > high then
        return high
    end
    return value
end

function bytes_to_string(bytes, length)
    local text = ""
    for i = 1, length do
        text = text .. string.char(bytes[i])
    end
    return text
end

function short_text(text, limit)
    if text == nil then
        return "--"
    end
    if string.len(text) <= limit then
        return text
    end
    return string.sub(text, 1, limit)
end

function ui_text(control_id, text)
    if text == nil then
        text = "--"
    end
    set_text(SCREEN_ID, control_id, text)
end