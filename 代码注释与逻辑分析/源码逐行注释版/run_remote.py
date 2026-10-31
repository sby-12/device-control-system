# -*- coding: utf-8 -*-
"""通过 paramiko 在 SeeAuto 云服务器上执行命令的小工具。
用法:
    python run_remote.py "要执行的命令"
    python run_remote.py            # 不带参数则进入简单的交互式 shell
"""
import sys
import paramiko

HOST = "connect.cqa1.seetacloud.com"
PORT = 46190
USER = "root"
PASSWORD = "kr4Ag2Y8TmMs"


def get_client():
    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    client.connect(HOST, port=PORT, username=USER, password=PASSWORD,
                   timeout=15, banner_timeout=15, auth_timeout=15)
    return client


def run_command(client, cmd):
    stdin, stdout, stderr = client.exec_command(cmd, timeout=300)
    out = stdout.read().decode("utf-8", errors="replace")
    err = stderr.read().decode("utf-8", errors="replace")
    return stdout.channel.recv_exit_status(), out, err


def interactive_shell(client):
    chan = client.invoke_shell(term="xterm")
    import select
    import socket
    print("已连接 %s，输入 exit 退出。" % HOST)
    try:
        while True:
            r, _, _ = select.select([chan, sys.stdin], [], [])
            if chan in r:
                data = chan.recv(4096).decode("utf-8", errors="replace")
                if not data:
                    break
                sys.stdout.write(data)
                sys.stdout.flush()
            if sys.stdin in r:
                line = sys.stdin.readline()
                if not line:
                    break
                chan.send(line)
    except (KeyboardInterrupt, socket.error):
        pass
    finally:
        chan.close()


def main():
    client = get_client()
    try:
        if len(sys.argv) > 1:
            code, out, err = run_command(client, " ".join(sys.argv[1:]))
            if out:
                print(out, end="")
            if err:
                print(err, end="", file=sys.stderr)
            sys.exit(code)
        else:
            interactive_shell(client)
    finally:
        client.close()


if __name__ == "__main__":
    main()
