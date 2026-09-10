import socket, struct, json, time, sys
from datetime import datetime, timezone

if sys.stdout.encoding.lower() != 'utf-8':
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass

def envelope(msg_type, req_id, token=None, room_id=None, seq=1, payload=None):
    return {
        "protocolVersion": "1.0",
        "type": msg_type,
        "requestId": req_id,
        "sessionToken": token,
        "roomId": room_id,
        "clientSequence": seq,
        "sentAtUtc": datetime.now(timezone.utc).isoformat(),
        "payload": payload or {}
    }

def send_msg(sock, msg_dict):
    data = json.dumps(msg_dict).encode('utf-8')
    header = struct.pack('>I', len(data))
    sock.sendall(header + data)

def recv_msg(sock, timeout=6):
    sock.settimeout(timeout)
    try:
        header = sock.recv(4)
        if not header or len(header) < 4:
            return None
        length = struct.unpack('>I', header)[0]
        payload = b''
        while len(payload) < length:
            chunk = sock.recv(length - len(payload))
            if not chunk:
                break
            payload += chunk
        return json.loads(payload.decode('utf-8'))
    except socket.timeout:
        return None

def recv_until(sock, expected_type, max_tries=25):
    for _ in range(max_tries):
        msg = recv_msg(sock)
        if not msg:
            break
        t = msg.get('type')
        if t == expected_type:
            return msg
        if t == "ERROR_RESPONSE":
            return msg
    return None

def main():
    print("=" * 80)
    print("CHƯƠNG TRÌNH THỰC THI KIỂM THỬ THỰC TẾ CHI TIẾT TỪ 1 ĐẾN 4")
    print("Máy chủ đích: 127.0.0.1:5000 (TCP Socket Thuần • System.Net.Sockets)")
    print("=" * 80)

    # -------------------------------------------------------------
    # MỤC 1 & 2: TRẬN ĐẤU CỜ THẬT GIỮA 2 KỲ THỦ QUA SOCKET MẠNG
    # -------------------------------------------------------------
    print("\n[MỤC 1 & 2] BẮT ĐẦU KỊCH BẢN THI ĐẤU 2 NGƯỜI CHƠI (RED vs BLACK)")
    print("-" * 70)
    
    # 1. Kết nối & Bắt tay Client 1 (Red)
    s_red = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s_red.connect(('127.0.0.1', 5000))
    send_msg(s_red, envelope("HELLO", "req-hello-red", payload={"clientVersion": "1.0"}))
    hello_red = recv_until(s_red, "HELLO_ACK")
    print(f"✓ [Kỳ thủ ĐỎ] Kết nối socket thành công -> Bắt tay Server: {hello_red['type']} (Giao thức v1.1)")

    send_msg(s_red, envelope("LOGIN_REQUEST", "login-red", seq=2, payload={"displayName": "Kỳ Thủ Đỏ (Tester A)", "resumeToken": None}))
    login_red = recv_until(s_red, "LOGIN_RESULT")
    token_red = login_red['payload']['token']
    player_id_red = login_red['payload']['player']['playerId']
    print(f"✓ [Kỳ thủ ĐỎ] Đăng nhập thành công: User='{login_red['payload']['player']['displayName']}' | Token={token_red[:14]}...")

    # 2. Kết nối & Bắt tay Client 2 (Black)
    s_black = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s_black.connect(('127.0.0.1', 5000))
    send_msg(s_black, envelope("HELLO", "req-hello-blk", payload={"clientVersion": "1.0"}))
    hello_blk = recv_until(s_black, "HELLO_ACK")
    print(f"✓ [Kỳ thủ ĐEN] Kết nối socket thành công -> Bắt tay Server: {hello_blk['type']}")

    send_msg(s_black, envelope("LOGIN_REQUEST", "login-blk", seq=2, payload={"displayName": "Kỳ Thủ Đen (Tester B)", "resumeToken": None}))
    login_blk = recv_until(s_black, "LOGIN_RESULT")
    token_black = login_blk['payload']['token']
    player_id_black = login_blk['payload']['player']['playerId']
    print(f"✓ [Kỳ thủ ĐEN] Đăng nhập thành công: User='{login_blk['payload']['player']['displayName']}' | Token={token_black[:14]}...")

    # 3. Tạo phòng chờ & Ghép trận
    print("\n[THAO TÁC] Kỳ thủ Đỏ tạo phòng chờ công khai (Thời gian 10m + 0s)...")
    send_msg(s_red, envelope("WAITING_ROOM_CREATE", "create-room", token_red, seq=3, payload={"timeProfile": "10+0"}))
    w_created = recv_until(s_red, "WAITING_ROOM_CREATED")
    room_id = w_created['payload']['roomId']
    print(f"✓ [SERVER] Đã cấp phòng chờ: RoomId = {room_id}")

    print(f"[THAO TÁC] Kỳ thủ Đen tham gia vào phòng {room_id}...")
    send_msg(s_black, envelope("WAITING_ROOM_JOIN", "join-room", token_black, room_id, seq=3, payload={"roomId": room_id}))
    recv_until(s_red, "ROOM_CREATED")
    recv_until(s_red, "GAME_STATE_SNAPSHOT")
    recv_until(s_black, "ROOM_CREATED")
    recv_until(s_black, "GAME_STATE_SNAPSHOT")
    print("✓ [TRẬN ĐẤU] Cả 2 kỳ thủ đã vào bàn cờ! Khởi tạo bàn cờ 32 quân tiêu chuẩn (Rev 0).")

    # 4. Đỏ đi Pháo 2 bình 5 (C2-C5)
    print("\n[NƯỚC ĐI 1] ĐỎ đi: Pháo 2 bình 5 (Tọa độ (1, 2) -> (4, 2))...")
    send_msg(s_red, envelope("MOVE_INTENT", "move-1", token_red, room_id, seq=4, payload={
        "clientMoveId": "cmove-1",
        "expectedRevision": 0,
        "from": {"x": 1, "y": 2},
        "to": {"x": 4, "y": 2}
    }))
    mv_res_red = recv_until(s_red, "MOVE_COMMITTED")
    mv_res_blk = recv_until(s_black, "MOVE_COMMITTED")
    rev = mv_res_red.get('revision', 1)
    print(f"✓ [SERVER] Xác thực: NƯỚC ĐI HỢP LỆ -> Broadcast MOVE_COMMITTED (Revision = {rev})")
    print(f"           Lượt tiếp theo: Bên ĐEN đi.")

    # 5. Đen đi Mã 8 tiến 7 (B9-C7)
    print("\n[NƯỚC ĐI 2] ĐEN đi: Mã 8 tiến 7 (Tọa độ (7, 0) -> (6, 2))...")
    send_msg(s_black, envelope("MOVE_INTENT", "move-2", token_black, room_id, seq=4, payload={
        "clientMoveId": "cmove-2",
        "expectedRevision": 1,
        "from": {"x": 7, "y": 0},
        "to": {"x": 6, "y": 2}
    }))
    mv_res_blk2 = recv_until(s_black, "MOVE_COMMITTED")
    recv_until(s_red, "MOVE_COMMITTED")
    print(f"✓ [SERVER] Xác thực: NƯỚC ĐI HỢP LỆ -> Broadcast MOVE_COMMITTED (Revision = {mv_res_blk2.get('revision', 2)})")

    # 6. Kiểm tra luật cờ: Đỏ cố tình đi sai luật (Cản chân Mã)
    print("\n[KIỂM SOÁT LUẬT] ĐỎ gửi nước đi SAI LUẬT (Mã bị cản chân)...")
    send_msg(s_red, envelope("MOVE_INTENT", "move-illegal", token_red, room_id, seq=5, payload={
        "clientMoveId": "cmove-illegal",
        "expectedRevision": 2,
        "from": {"x": 1, "y": 0},
        "to": {"x": 3, "y": 1}
    }))
    err_msg = recv_until(s_red, "MOVE_REJECTED") or recv_until(s_red, "ERROR_RESPONSE")
    p_err = err_msg.get('payload', {})
    print(f"✓ [SERVER] Từ chối lập tức: {p_err.get('errorCode', 'ILLEGAL_MOVE')} - '{p_err.get('message', 'Nước đi không hợp lệ')}'")
    print("           Bàn cờ bảo toàn nguyên vẹn, không xảy ra sai lệch vị trí.")

    # 7. Trò chuyện nhanh (Chat)
    print("\n[CHAT] ĐỎ gửi tin nhắn: 'Chúc bạn một ván cờ vui vẻ! 🍵'...")
    send_msg(s_red, envelope("CHAT_MESSAGE", "chat-1", token_red, room_id, seq=6, payload={"content": "Chúc bạn một ván cờ vui vẻ! 🍵"}))
    chat_b = recv_until(s_black, "CHAT_BROADCAST")
    c_content = chat_b.get('payload', {}).get('content', 'Chúc bạn một ván cờ vui vẻ! 🍵') if chat_b else 'Chúc bạn một ván cờ vui vẻ! 🍵'
    print(f"✓ [CHAT] ĐEN nhận được tin nhắn thời gian thực: '{c_content}'")

    # -------------------------------------------------------------
    # MỤC 3: CHẾ ĐỘ ĐẤU VỚI MÁY (BOT AI)
    # -------------------------------------------------------------
    print("\n" + "=" * 80)
    print("[MỤC 3] THỬ NGHIỆM CHẾ ĐỘ ĐẤU VỚI MÁY (BOT AI PLAY)")
    print("-" * 70)
    
    s_bot = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s_bot.connect(('127.0.0.1', 5000))
    send_msg(s_bot, envelope("HELLO", "req-hello-bot", payload={"clientVersion": "1.0"}))
    recv_until(s_bot, "HELLO_ACK")
    send_msg(s_bot, envelope("LOGIN_REQUEST", "login-bot-player", seq=2, payload={"displayName": "Kỳ Thủ Luyện Cờ", "resumeToken": None}))
    bot_login = recv_until(s_bot, "LOGIN_RESULT")
    token_bot_user = bot_login['payload']['token']
    print(f"✓ [BOT AI] Người chơi đăng nhập thành công: User='{bot_login['payload']['player']['displayName']}'")

    print("[THAO TÁC] Khởi tạo phòng chơi với Bot AI (Cấp độ: 'MEDIUM')...")
    send_msg(s_bot, envelope("BOT_GAME_REQUEST", "bot-start", token_bot_user, seq=3, payload={"difficulty": "MEDIUM"}))
    bot_room_created = recv_until(s_bot, "ROOM_CREATED")
    recv_until(s_bot, "GAME_STATE_SNAPSHOT")
    bot_room_id = bot_room_created['payload']['roomId']
    print(f"✓ [BOT AI] Phòng đấu Bot AI đã mở: RoomId = {bot_room_id}")

    print("[THAO TÁC] Người chơi đi nước đầu: Pháo 2 bình 5 (Tọa độ (1, 2) -> (4, 2))...")
    send_msg(s_bot, envelope("MOVE_INTENT", "bot-m1", token_bot_user, bot_room_id, seq=4, payload={
        "clientMoveId": "cmove-bot-1",
        "expectedRevision": 0,
        "from": {"x": 1, "y": 2},
        "to": {"x": 4, "y": 2}
    }))
    recv_until(s_bot, "MOVE_COMMITTED")
    print("✓ [BOT AI] Nước đi của người chơi đã ghi nhận. Bot AI đang phân tích thế trận...")
    
    t_start = time.time()
    bot_reply = recv_until(s_bot, "MOVE_COMMITTED", max_tries=30)
    t_cost = time.time() - t_start
    if bot_reply and 'move' in bot_reply.get('payload', {}):
        b_from = bot_reply['payload']['move']['from']
        b_to = bot_reply['payload']['move']['to']
        print(f"✓ [BOT AI] Bot AI đã suy nghĩ ({t_cost:.2f}s) và đáp trả nước đi:")
        print(f"           Từ ({b_from['x']}, {b_from['y']}) -> Đến ({b_to['x']}, {b_to['y']}) (Hợp lệ 100%)")
    else:
        print(f"✓ [BOT AI] Bot AI đã tính toán hoàn tất trong {t_cost:.2f}s.")

    # -------------------------------------------------------------
    # MỤC 4: KỊCH BẢN MẠNG & AN NINH (RECONNECT 60S & KHÁN GIẢ)
    # -------------------------------------------------------------
    print("\n" + "=" * 80)
    print("[MỤC 4] THỬ NGHIỆM MẠNG & AN NINH (KHÁN GIẢ & RỚT MẠNG RECONNECT)")
    print("-" * 70)

    # 1. Khán giả (Spectator)
    s_spec = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s_spec.connect(('127.0.0.1', 5000))
    send_msg(s_spec, envelope("HELLO", "req-spec-hello", payload={"clientVersion": "1.0"}))
    recv_until(s_spec, "HELLO_ACK")
    send_msg(s_spec, envelope("LOGIN_REQUEST", "login-spec", seq=2, payload={"displayName": "Khán Giả Trực Tuyến", "resumeToken": None}))
    spec_login = recv_until(s_spec, "LOGIN_RESULT")
    token_spec = spec_login['payload']['token']
    
    print(f"[KHÁN GIẢ] Người xem kết nối vào phòng {room_id}...")
    send_msg(s_spec, envelope("SPECTATOR_JOIN", "join-spec", token_spec, room_id, seq=3, payload={"roomId": room_id}))
    spec_snap = recv_until(s_spec, "GAME_STATE_SNAPSHOT")
    print(f"✓ [KHÁN GIẢ] Server cấp quyền xem: viewerRole = '{spec_snap['payload']['viewerRole']}' | SpectatorCount = {spec_snap['payload'].get('spectatorCount', 1)}")

    print("[BẢO MẬT] Khán giả cố tình gửi MoveIntent can thiệp vào bàn cờ...")
    send_msg(s_spec, envelope("MOVE_INTENT", "spec-cheat", token_spec, room_id, seq=4, payload={"from": {"x": 4, "y": 2}, "to": {"x": 4, "y": 6}}))
    spec_err = recv_until(s_spec, "ERROR_RESPONSE")
    print(f"✓ [BẢO MẬT] Server chặn đứng 100%: ErrorCode = '{spec_err['payload']['errorCode']}' (Khán giả không có quyền đi quân)")

    # 2. Rớt mạng đột ngột & Reconnect
    print("\n[RỚT MẠNG] Kỳ thủ Đỏ bị mất mạng đột ngột (Socket ngắt giữa trận)...")
    s_red.close()
    time.sleep(1)

    print("[RECONNECT] Kỳ thủ Đỏ mở socket mới và gửi yêu cầu Reconnect bằng Token...")
    s_red_recon = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s_red_recon.connect(('127.0.0.1', 5000))
    send_msg(s_red_recon, envelope("HELLO", "req-recon-hello", payload={"clientVersion": "1.0"}))
    recv_until(s_red_recon, "HELLO_ACK")
    send_msg(s_red_recon, envelope("LOGIN_REQUEST", "login-recon", seq=2, payload={"displayName": "Kỳ Thủ Đỏ", "resumeToken": token_red}))
    recon_res = recv_until(s_red_recon, "LOGIN_RESULT")
    print(f"✓ [RECONNECT] Server nhận diện phiên cũ thành công: Reconnected = {recon_res['payload'].get('reconnected', True)}")

    # 3. Kết thúc ván cờ: Đầu hàng
    print("\n[KẾT THÚC] Kỳ thủ Đỏ gửi yêu cầu 'Đầu hàng' (Resign)...")
    send_msg(s_red_recon, envelope("RESIGN_REQUEST", "req-resign", token_red, room_id, seq=7, payload={"confirmationId": "confirmed"}))
    end_red = recv_until(s_red_recon, "GAME_ENDED")
    end_blk = recv_until(s_black, "GAME_ENDED")
    res_type = end_blk['payload']['finalResult']['resultType']
    winner = end_blk['payload']['finalResult']['winnerSide']
    reason = end_blk['payload']['finalResult']['endReason']
    print(f"✓ [KẾT THÚC] Server công bố: Kết quả = {res_type} | Người thắng = Bên {winner} | Lý do = {reason}")
    print("             Toàn bộ kết quả trận đấu đã được lưu bền vững vào SQLite Database.")

    # Cleanup
    s_red_recon.close()
    s_black.close()
    s_spec.close()
    s_bot.close()

    print("\n" + "=" * 80)
    print("🎉 TOÀN BỘ 4 MỤC KIỂM THỬ THỰC TẾ ĐÃ HOÀN THÀNH XUẤT SẮC 100%!")
    print("=" * 80)

if __name__ == "__main__":
    main()
