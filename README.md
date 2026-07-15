# LBP Archive Toolkit
This app allows you to search the LBP archive for levels to download and backup. It also features a built-in backup manager to securely manage, edit, and organize the levels you've downloaded locally.

## Prerequisites
- **Windows 10 or 11** (x64)
- **.NET 11 preview Desktop Runtime** (if you don't have it, you'll be prompted to download it from Microsoft when you start the app)
  
## How to Use
1. [Download](https://github.com/GigsTheCat/LBP_Archive_Toolkit/releases) the latest `.zip` release, extract it, and place the app folder anywhere.
2. Start `LBP Archive Toolkit.exe`. *(If Windows Defender SmartScreen warns you about an unrecognized app, click "More info" -> "Run anyway").*
3. Go to **Settings** and choose the location of your `dry.db` or `fastdry.db` file. (If you don't have this, go to Info > Downloads and choose the version you want).
4. Choose your backup location. *(If you are using RPCS3, it is recommended to set this to your RPCS3 savedata folder: `rpcs3 > dev_hdd0 > home > 00000001 > savedata`)*.
5. Choose your download server (note: `bonsai` and `archive` servers are currently rate-limited/slow).
6. **Local Archives:** If you have a local copy of the full 1.2 TB level archive, you can select `local` as your download server and point the app to your archive folder. (Unzip all the folders to make indexing faster).
7. Save your settings and have fun!

<img width="1617" height="960" alt="image" src="https://github.com/user-attachments/assets/b7e58aa6-689d-45c9-8472-31b15b2c62b7" />





## How to Build (From Source)

If you'd like to compile the application yourself instead of downloading the pre-packaged release, follow these steps:

### Requirements
* [Visual Studio 2026](https://visualstudio.microsoft.com/) (with the **.NET Desktop Development** workload and preview features enabled) OR the [.NET 11 preview SDK](https://dotnet.microsoft.com/en-us/download/dotnet/11.0) if using the command line.

### Command Line Instructions
1. Clone the repository:
   ```cmd
   git clone https://github.com/GigsTheCat/LBP_Archive_Toolkit.git
   cd LBP_Archive_Toolkit
2. Restore dependencies and build the project:
   ```cmd
   dotnet build -c Release

3. (Optional) To create a compiled single-file executable exactly like the official releases:
   ```cmd
   dotnet publish -c Release -r win-x64 -o ./PublishOutput /p:PublishSingleFile=true
  Your compiled .exe will be located in the ./PublishOutput folder.

## Notes
Currently Windows only. 

## Credits
Some code is heavily based on / reverse engineered from [lbp_archive_dl](https://github.com/uhwot/lbp_archive_dl) and cwlib, but also with several improvements, modernizations, and fixes.
The contributor data was obtained using cwlib.
