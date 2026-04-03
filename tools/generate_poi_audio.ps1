$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Speech

$outputDir = "d:\Do An Thuyet Minh Project\Do An Thuyet Minh\Do An Thuyet Minh\TourGuideApp2\Resources\Raw\audio\vi"
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

$items = @(
    @{
        File = "poi_0_oc_dao.wav"
        Text = "Bạn đang ở khu vực ốc Đào Vĩnh Khánh. Món ốc có vị ngọt, dai và thơm đặc trưng. Hãy thử ốc hấp hoặc ốc xào để cảm nhận trọn vị hải sản nơi đây."
    },
    @{
        File = "poi_1_oc_sau_no.wav"
        Text = "Ốc Sầu Nơ nổi tiếng với vị béo ngậy và độ giòn thơm. Bạn có thể ăn kèm nước chấm đậm vị để món ăn dậy hương và hấp dẫn hơn."
    },
    @{
        File = "poi_2_oc_thao.wav"
        Text = "Ốc Thao có hương vị lạ miệng, dễ ăn và rất hợp để khám phá món mới. Mùi thơm nhẹ và vị ngọt tự nhiên sẽ khiến bạn muốn ăn thêm."
    },
    @{
        File = "poi_3_oc_vu.wav"
        Text = "Ốc Vũ được nhiều thực khách yêu thích nhờ thơm ngon, giòn dai và vị đậm đà. Bạn hãy thử theo cách chế biến của quán để trải nghiệm đa dạng hơn."
    },
    @{
        File = "poi_4_oc_oanh.wav"
        Text = "Bạn đang ở khu vực Ốc Oanh nổi tiếng nhất phố Vĩnh Khánh. Quán có hơn 20 năm tuổi, ốc tươi ngon, nước chấm đậm đà. Hãy thử ốc hương xào bơ hoặc càng ghẹ rang muối ớt để cảm nhận vị hải sản đặc trưng Sài Gòn."
    },
    @{
        File = "poi_5_oc_phat.wav"
        Text = "Bạn đang ở khu vực Ốc Phát nổi tiếng trên phố Vĩnh Khánh. Hải sản tươi sống, giá bình dân. Hãy thử ốc hương xào bơ tỏi, ốc len xào dừa hoặc càng cua rang me để thưởng thức vị hải sản đậm đà Sài Gòn."
    }
)

foreach ($item in $items) {
    $synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
    $synth.Rate = 0
    $synth.Volume = 100

    $target = Join-Path $outputDir $item.File
    $synth.SetOutputToWaveFile($target)
    $synth.Speak($item.Text)
    $synth.Dispose()
}

Write-Output "Generated $($items.Count) wav files in $outputDir"
