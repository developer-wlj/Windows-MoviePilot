# 基于v1.2.1版本制作的示例, 不可复制贴贴, 因为后续版本可能存在不同, 只能一项一项的对比修改
# 填写的值只要不是None False True 和数字, 其他的值统统需要加""进行包裹
# 变量不懂的 可以前往 https://github.com/jxxghp/MoviePilot/blob/main/README.md#%E9%85%8D%E7%BD%AE 查看更详细的描述

import secrets
from pathlib import Path
from typing import List

from pydantic import BaseSettings


class Settings(BaseSettings):
    # 项目名称 不用动它
    PROJECT_NAME = "MoviePilot"
    # API路径 不用动它
    API_V1_STR: str = "/api/v1"
    # 密钥 不用动它
    SECRET_KEY: str = secrets.token_urlsafe(32)
    # 允许的域名 不用动它
    ALLOWED_HOSTS: list = ["*"]
    # TOKEN过期时间 不用动它
    ACCESS_TOKEN_EXPIRE_MINUTES: int = 60 * 24 * 8
    # 时区 不用动它
    TZ: str = "Asia/Shanghai"
    # API监听地址 不用动它
    HOST: str = "0.0.0.0"
    # API监听端口 除非你会nginx设置 一般不用动它
    PORT: int = 3001
    # 是否调试模式 True为打开 会打印更详细的日志
    DEBUG: bool = False
    # 是否开发模式 非开发者不用动它
    DEV: bool = False
    # 配置文件目录 此目录指的是生成user.db所在的目录 非环境配置目录 windows下可以不用填写,如要填写应为 r"C:\dir" 形式
    # 比如 MoviePilot安装在D盘, 你想指定user.db生成在F盘的config目录,就是 CONFIG_DIR: str = r"F:\config"
    CONFIG_DIR: str = None
    # 超级管理员 内网下不用动它 公网用户建议修改
    SUPERUSER: str = "admin"
    # 超级管理员初始密码  内网下不用动它 公网用户建议修改
    SUPERUSER_PASSWORD: str = "password"
    # API密钥，需要更换 公网用户建议修改
    API_TOKEN: str = "moviepilot"
    # 网络代理 IP:PORT 设置成http代理 如 PROXY_HOST: str = "127.0.0.1:10086"
    PROXY_HOST: str = None
    # 媒体信息搜索来源 不用动它
    SEARCH_SOURCE: str = "themoviedb"
    # 刮削入库的媒体文件 字面意思 如果不想让MoviePilot刮削 让emby/jellyfin/plex自己刮削 就设为False
    SCRAP_METADATA: bool = True
    # 新增已入库媒体是否跟随TMDB信息变化 不用动它
    SCRAP_FOLLOW_TMDB: bool = True
    # 刮削来源 不用动它
    SCRAP_SOURCE: str = "themoviedb"
    # TMDB图片地址 不用动它
    TMDB_IMAGE_DOMAIN: str = "image.tmdb.org"
    # TMDB API地址 不用动它
    TMDB_API_DOMAIN: str = "api.themoviedb.org"
    # TMDB API Key 可以更换成自己申请的key
    TMDB_API_KEY: str = "db55323b8d3e4154498498a75642b381"
    # TVDB API Key 可以更换成自己申请的key
    TVDB_API_KEY: str = "6b481081-10aa-440c-99f2-21d17717ee02"
    # Fanart API Key 可以更换成自己申请的key
    FANART_API_KEY: str = "d2d31f9ecabea050fc7d68aa3146015f"
    # 支持的后缀格式 不用动它
    RMT_MEDIAEXT: list = ['.mp4', '.mkv', '.ts', '.iso',
                          '.rmvb', '.avi', '.mov', '.mpeg',
                          '.mpg', '.wmv', '.3gp', '.asf',
                          '.m4v', '.flv', '.m2ts', '.strm',
                          '.tp']
    # 支持的字幕文件后缀格式 不用动它
    RMT_SUBEXT: list = ['.srt', '.ass', '.ssa']
    # 支持的音轨文件后缀格式 不用动它
    RMT_AUDIO_TRACK_EXT: list = ['.mka']
    # 索引器 不用动它
    INDEXER: str = "builtin"
    # 订阅模式 不用动它
    SUBSCRIBE_MODE: str = "spider"
    # RSS订阅模式刷新时间间隔（分钟） 可以自己设定 符合订阅站点rss要求的时间间隔即可
    SUBSCRIBE_RSS_INTERVAL: int = 30
    # 订阅搜索开关 字面意思
    SUBSCRIBE_SEARCH: bool = False
    # 用户认证站点 hhclub/audiences/hddolby/zmpt/freefarm/hdfans/wintersakura/leaves/1ptba/icc2022/iyuu/ptlsp
    AUTH_SITE: str = ""
    # iyuu
    IYUU_SIGN: str = ""
    # hhclub
    HHCLUB_USERNAME: str = ""
    HHCLUB_PASSKEY: str = ""
    # audiences
    AUDIENCES_UID: str = ""
    AUDIENCES_PASSKEY: str = ""
    # hddolby
    HDDOLBY_ID: str = ""
    HDDOLBY_PASSKEY: str = ""
    # hddolby
    ZMPT_UID: str = ""
    ZMPT_PASSKEY: str = ""
    # freefarm
    FREEFARM_UID: str = ""
    FREEFARM_PASSKEY: str = ""
    # hdfans
    HDFANS_UID: str = ""
    HDFANS_PASSKEY: str = ""
    # wintersakura
    WINTERSAKURA_UID: str = ""
    WINTERSAKURA_PASSKEY: str = ""
    # leaves
    LEAVES_UID: str = ""
    LEAVES_PASSKEY: str = ""
    # 1ptba
    ONEPTBA_UID: str = ""
    ONEPTBA_PASSKEY: str = ""
    # icc2022
    ICC2022_UID: str = ""
    ICC2022_PASSKEY: str = ""
    # ptlsp
    PTLSP_UID: str = ""
    PTLSP_PASSKEY: str = ""
    # 交互搜索自动下载用户ID，使用,分割 如 AUTO_DOWNLOAD_USER: str = "1111,2222"
    AUTO_DOWNLOAD_USER: str = None
    # 消息通知渠道 telegram/wechat/slack
    MESSAGER: str = "telegram"
    # WeChat企业ID 填写的值用""包裹
    WECHAT_CORPID: str = None
    # WeChat应用Secret 填写的值用""包裹
    WECHAT_APP_SECRET: str = None
    # WeChat应用ID 填写的值用""包裹
    WECHAT_APP_ID: str = None
    # WeChat代理服务器 内网用户就使用官方接口 公网用户看文档 设置nginx反代
    WECHAT_PROXY: str = "https://qyapi.weixin.qq.com"
    # WeChat Token 填写的值用""包裹
    WECHAT_TOKEN: str = None
    # WeChat EncodingAESKey 填写的值用""包裹
    WECHAT_ENCODING_AESKEY: str = None
    # WeChat 管理员 填写的值用""包裹
    WECHAT_ADMINS: str = None
    # Telegram Bot Token 填写的值用""包裹
    TELEGRAM_TOKEN: str = None
    # Telegram Chat ID 填写的值用""包裹
    TELEGRAM_CHAT_ID: str = None
    # Telegram 用户ID，使用,分隔
    TELEGRAM_USERS: str = ""
    # Telegram 管理员ID，使用,分隔
    TELEGRAM_ADMINS: str = ""
    # Slack Bot User OAuth Token
    SLACK_OAUTH_TOKEN: str = ""
    # Slack App-Level Token
    SLACK_APP_TOKEN: str = ""
    # Slack 频道名称
    SLACK_CHANNEL: str = ""
    # 下载器 qbittorrent/transmission
    DOWNLOADER: str = "qbittorrent"
    # 下载器监控开关 建议下载器监控和插件的目录监控 只开一个,否则可能会有多条重复的通知
    DOWNLOADER_MONITOR: bool = True
    # Qbittorrent地址，IP:PORT 填写的值用""包裹
    QB_HOST: str = None
    # Qbittorrent用户名 没有用户名和密码的可以不用填写 填写的值用""包裹
    QB_USER: str = None
    # Qbittorrent密码 没有用户名和密码的可以不用填写 填写的值用""包裹
    QB_PASSWORD: str = None
    # Qbittorrent分类自动管理
    QB_CATEGORY: bool = False
    # Transmission地址，IP:PORT 填写的值用""包裹
    TR_HOST: str = None
    # Transmission用户名 填写的值用""包裹
    TR_USER: str = None
    # Transmission密码 填写的值用""包裹
    TR_PASSWORD: str = None
    # 种子标签
    TORRENT_TAG: str = "MOVIEPILOT"
    # 下载保存目录，容器内映射路径需要一致 windows下 填写形式应为 r"C:\dir"
    DOWNLOAD_PATH: str = r"C:\Users\Default\Downloads"
    # 电影下载保存目录，容器内映射路径需要一致 windows下 填写形式应为 r"C:\dir"
    DOWNLOAD_MOVIE_PATH: str = None
    # 电视剧下载保存目录，容器内映射路径需要一致 windows下 填写形式应为 r"C:\dir"
    DOWNLOAD_TV_PATH: str = None
    # 动漫下载保存目录，容器内映射路径需要一致 windows下 填写形式应为 r"C:\dir"
    DOWNLOAD_ANIME_PATH: str = None
    # 下载目录二级分类 不用动它
    DOWNLOAD_CATEGORY: bool = False
    # 下载站点字幕 如果你下载的影片都有内嵌字幕 可以设为False 关闭改选项
    DOWNLOAD_SUBTITLE: bool = True
    # 媒体服务器 emby/jellyfin/plex
    MEDIASERVER: str = "emby"
    # 入库刷新媒体库
    REFRESH_MEDIASERVER: bool = True
    # 媒体服务器同步间隔（小时）
    MEDIASERVER_SYNC_INTERVAL: int = 6
    # EMBY服务器地址，IP:PORT 填写的值用""包裹
    EMBY_HOST: str = None
    # EMBY Api Key 填写的值用""包裹
    EMBY_API_KEY: str = None
    # Jellyfin服务器地址，IP:PORT 填写的值用""包裹
    JELLYFIN_HOST: str = None
    # Jellyfin Api Key 填写的值用""包裹
    JELLYFIN_API_KEY: str = None
    # Plex服务器地址，IP:PORT 填写的值用""包裹
    PLEX_HOST: str = None
    # Plex Token 填写的值用""包裹
    PLEX_TOKEN: str = None
    # 转移方式 link/copy/move/softlink
    TRANSFER_TYPE: str = "copy"
    # CookieCloud服务器地址
    COOKIECLOUD_HOST: str = "https://movie-pilot.org/cookiecloud"
    # CookieCloud用户KEY 填写的值用""包裹
    COOKIECLOUD_KEY: str = None
    # CookieCloud端对端加密密码 填写的值用""包裹
    COOKIECLOUD_PASSWORD: str = None
    # CookieCloud同步间隔（分钟）
    COOKIECLOUD_INTERVAL: int = 60 * 24
    # OCR服务器地址
    OCR_HOST: str = "https://movie-pilot.org"
    # CookieCloud对应的浏览器UA
    USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/113.0.0.0 Safari/537.36 Edg/113.0.1774.57"
    # 媒体库目录，多个目录使用,分隔 windows下 填写形式应为 r"C:\dir" 或 r"C:\dir,C:\dir01"
    # 首页不显示存储空间大小 就是该变量没设置
    LIBRARY_PATH: str = None
    # 电影媒体库目录名，默认"电影"
    LIBRARY_MOVIE_NAME: str = None
    # 电视剧媒体库目录名，默认"电视剧"
    LIBRARY_TV_NAME: str = None
    # 动漫媒体库目录名，默认"电视剧/动漫"
    LIBRARY_ANIME_NAME: str = None
    # 二级分类 配置文件在MoviePilot后端项目目录-config-category.yaml 如果用户指定了CONFIG_DIR 就是CONFIG_DIR目录下category.yaml
    LIBRARY_CATEGORY: bool = True
    # 电视剧动漫的分类genre_ids
    ANIME_GENREIDS = [16]
    # 电影重命名格式
    MOVIE_RENAME_FORMAT: str = "{{title}}{% if year %} ({{year}}){% endif %}" \
                               "/{{title}}{% if year %} ({{year}}){% endif %}{% if part %}-{{part}}{% endif %}{% if videoFormat %} - {{videoFormat}}{% endif %}" \
                               "{{fileExt}}"
    # 电视剧重命名格式
    TV_RENAME_FORMAT: str = "{{title}}{% if year %} ({{year}}){% endif %}" \
                            "/Season {{season}}" \
                            "/{{title}} - {{season_episode}}{% if part %}-{{part}}{% endif %}{% if episode %} - 第 {{episode}} 集{% endif %}" \
                            "{{fileExt}}"
    # 大内存模式 True为开启
    BIG_MEMORY_MODE: bool = False

    @property
    def INNER_CONFIG_PATH(self):
        return self.ROOT_PATH / "config"

    @property
    def CONFIG_PATH(self):
        if self.CONFIG_DIR:
            return Path(self.CONFIG_DIR)
        return self.INNER_CONFIG_PATH

    @property
    def TEMP_PATH(self):
        return self.CONFIG_PATH / "temp"

    @property
    def ROOT_PATH(self):
        return Path(__file__).parents[2]

    @property
    def PLUGIN_DATA_PATH(self):
        return self.CONFIG_PATH / "plugins"

    @property
    def LOG_PATH(self):
        return self.CONFIG_PATH / "logs"

    @property
    def CACHE_CONF(self):
        if self.BIG_MEMORY_MODE:
            return {
                "tmdb": 1024,
                "refresh": 50,
                "torrents": 100,
                "douban": 512,
                "fanart": 512,
                "meta": 15 * 24 * 3600
            }
        return {
            "tmdb": 256,
            "refresh": 30,
            "torrents": 50,
            "douban": 256,
            "fanart": 128,
            "meta": 7 * 24 * 3600
        }

    @property
    def PROXY(self):
        if self.PROXY_HOST:
            return {
                "http": self.PROXY_HOST,
                "https": self.PROXY_HOST,
            }
        return None

    @property
    def PROXY_SERVER(self):
        if self.PROXY_HOST:
            return {
                "server": self.PROXY_HOST
            }

    @property
    def LIBRARY_PATHS(self) -> List[Path]:
        if self.LIBRARY_PATH:
            return [Path(path) for path in self.LIBRARY_PATH.split(",")]
        return []

    def __init__(self):
        super().__init__()
        with self.CONFIG_PATH as p:
            if not p.exists():
                p.mkdir(parents=True, exist_ok=True)
        with self.TEMP_PATH as p:
            if not p.exists():
                p.mkdir(parents=True, exist_ok=True)
        with self.LOG_PATH as p:
            if not p.exists():
                p.mkdir(parents=True, exist_ok=True)

    class Config:
        case_sensitive = True


settings = Settings()
