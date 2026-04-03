using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TourGuideApp2.Models;
using System.Collections.ObjectModel;

namespace TourGuideApp2.PageModels;

public partial class PlacesPageModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<Place> places = new();

    public PlacesPageModel()
    {
        LoadPlaces();
    }

    private void LoadPlaces()
    {
        // Danh sách POI (hardcode) cho khu phố ẩm thực Vĩnh Khánh.
        // Tọa độ đang lấy theo khu vực lân cận (đủ để biểu diễn POI trên bản đồ).
        Places = new ObservableCollection<Place>
        {
            new Place
            {
                Name = "Ốc Đào",
                Address = "Phố ẩm thực Vĩnh Khánh, Q4",
                Specialty = "Ốc Đào - hấp dẫn & thơm ngon",
                ImageUrl = "pho-am-thuc-vinh-khanh-oc-dao-1707245308.jpg",
                Latitude = 10.7582,
                Longitude = 106.7027,
                ActivationRadiusMeters = 34,
                Priority = 3,
                Description = "Bạn đang ở khu vực ốc Đào Vĩnh Khánh. Món ốc có vị ngọt, dai và thơm đặc trưng của hải sản. Hãy thử món ốc hấp/ốc xào tại đây để cảm nhận trọn vị.",
                VietnameseAudioText = "Bạn đang ở khu vực ốc Đào Vĩnh Khánh. Món ốc có vị ngọt, dai và thơm đặc trưng. Hãy thử ốc hấp hoặc ốc xào để cảm nhận trọn vị hải sản nơi đây.",
                EnglishAudioText = "You are now in the 'Oc Dao' area of Vinh Khanh food street. The clams are sweet, chewy, and have a distinctive seafood aroma. Try steamed clams or stir-fried clams here to enjoy the full flavor.",
                ChineseAudioText = "您正在永庆美食街的 Oc Đào 区域。贝类味道甜美、有嚼劲且带有独特的海鲜香气。请尝试清蒸贝或炒贝，以充分感受这里海鲜的完整风味。",
                JapaneseAudioText = "あなたは今、ヴィン・カイン美食街のOc Đàoエリアにいます。貝は甘く歯ごたえがあり、独特のシーフードの香りが特徴です。蒸し貝や炒め貝を試して、ここでシーフードの完全な味を味わってください。"
            },
            new Place
            {
                Name = "Ốc Sầu Nơ",
                Address = "Phố ẩm thực Vĩnh Khánh, Q4",
                Specialty = "Ốc Sầu Nơ - béo ngậy, đậm vị",
                ImageUrl = "pho-am-thuc-vinh-khanh-oc-sau-no-1707245308.jpg",
                Latitude = 10.7587,
                Longitude = 106.7021,
                ActivationRadiusMeters = 36,
                Priority = 2,
                Description = "Ốc Sầu Nơ nổi tiếng với vị béo ngậy và độ giòn, thơm. Bạn có thể ăn cùng nước chấm đậm vị để tăng hương vị cho món ăn.",
                VietnameseAudioText = "Ốc Sầu Nơ nổi tiếng với vị béo ngậy và độ giòn thơm. Bạn có thể ăn kèm nước chấm đậm vị để món ăn dậy hương và hấp dẫn hơn.",
                EnglishAudioText = "Oc Sau No is famous for its rich, creamy taste and fragrant crunch. Enjoy it with a flavorful dipping sauce to make the dish even more aromatic and satisfying.",
                ChineseAudioText = "Oc Sầu Nơ 以浓郁肥美的口感和脆香闻名。您可以搭配浓郁的蘸酱，让菜肴更加香气四溢且诱人。",
                JapaneseAudioText = "Oc Sầu Nơは濃厚でクリーミーな味わいとサクサク香ばしさが有名です。濃いめのタレと一緒に食べると、料理の香りがさらに引き立ち、美味しくなります。"
            },
            new Place
            {
                Name = "Ốc Thao",
                Address = "Phố ẩm thực Vĩnh Khánh, Q4",
                Specialty = "Ốc Thao - vị lạ, dễ ăn",
                ImageUrl = "pho-am-thuc-vinh-khanh-oc-thao-1707245333.jpg",
                Latitude = 10.7589,
                Longitude = 106.7018,
                ActivationRadiusMeters = 33,
                Priority = 1,
                Description = "Ốc Thao có hương vị lạ miệng, dễ ăn và rất hợp cho người muốn khám phá món mới. Mùi thơm và độ ngọt tự nhiên sẽ khiến bạn nhớ mãi.",
                VietnameseAudioText = "Ốc Thao có hương vị lạ miệng, dễ ăn và rất hợp để khám phá món mới. Mùi thơm nhẹ và vị ngọt tự nhiên sẽ khiến bạn muốn ăn thêm.",
                EnglishAudioText = "Oc Thao has a unique but easy-to-eat flavor, perfect for trying something new. Its mild aroma and natural sweetness will make you want another bite.",
                ChineseAudioText = "Oc Thao 口味独特、容易入口，非常适合探索新菜品。淡淡的香气和天然的甜味会让您想再吃一口。",
                JapaneseAudioText = "Oc Thaoは独特で食べやすい味わいがあり、新しい料理を探求するのにぴったりです。軽い香りと自然な甘さが、もう一口食べたくさせるでしょう。"
            },
            new Place
            {
                Name = "Ốc Vũ",
                Address = "Phố ẩm thực Vĩnh Khánh, Q4",
                Specialty = "Ốc Vũ - thơm, giòn & đậm đà",
                ImageUrl = "pho-am-thuc-vinh-khanh-oc-vu-1707245333.jpg",
                Latitude = 10.7594,
                Longitude = 106.7022,
                ActivationRadiusMeters = 35,
                Priority = 2,
                Description = "Ốc Vũ thường được yêu thích vì thơm ngon, độ giòn và vị đậm đà. Bạn có thể thử cách chế biến theo sở thích của quán để trải nghiệm đa dạng.",
                VietnameseAudioText = "Ốc Vũ được nhiều thực khách yêu thích nhờ thơm ngon, giòn dai và vị đậm đà. Bạn hãy thử theo cách chế biến của quán để trải nghiệm đa dạng hơn.",
                EnglishAudioText = "Oc Vu is loved for its delicious aroma, chewy crunch, and rich flavor. Follow the shop’s recommended style to experience more variety.",
                ChineseAudioText = "Oc Vũ 因其美味、脆嫩且浓郁的口味而深受许多食客喜爱。请按照店铺的烹饪方式尝试，以获得更多样化的体验。",
JapaneseAudioText = "Oc Vũは美味しく歯ごたえがあり濃厚な味わいで多くの客に愛されています。店の調理法に従って試してみてください。多様な体験ができます。"
            },
            new Place
            {
                Name = "Ốc Oanh",
                Address = "534 Vĩnh Khánh, Q4",
                Specialty = "Ốc Oanh - nổi tiếng nhất phố, tươi ngon chuẩn vị",
                ImageUrl = "oc-oanh.jpg",
                Latitude = 10.7590,
                Longitude = 106.7030,
                ActivationRadiusMeters = 38,
                Priority = 4,
                Description = "Ốc Oanh là quán ốc 'huyền thoại' của phố Vĩnh Khánh với hơn 20 năm tuổi đời, thậm chí được Michelin Selected công nhận. Ốc tươi, nước chấm đậm đà, các món như ốc hương xào bơ, càng ghẹ rang muối ớt hay bạch tuộc nướng đều cực kỳ được ưa chuộng.",
                VietnameseAudioText = "Bạn đang ở khu vực Ốc Oanh nổi tiếng nhất phố Vĩnh Khánh. Quán có hơn 20 năm tuổi, ốc tươi ngon, nước chấm đậm đà. Hãy thử ốc hương xào bơ hoặc càng ghẹ rang muối ớt để cảm nhận vị hải sản đặc trưng Sài Gòn.",
                EnglishAudioText = "You are now at the legendary Oc Oanh, the most famous snail spot on Vinh Khanh street for over 20 years, even selected by Michelin. Fresh snails, rich dipping sauces. Try butter-stir-fried snails or salted-chili grilled crab claws to taste authentic Saigon seafood.",
                ChineseAudioText = "您正在永庆街最著名的Oc Oanh区域。这家店已有超过20年的历史，贝类新鲜美味，蘸酱浓郁。请尝试黄油炒香螺或盐辣烤蟹爪，以感受西贡特色的海鲜风味。",
                JapaneseAudioText = "あなたは今、ヴィン・カイン通りで最も有名なOc Oanhエリアにいます。この店は20年以上続いており、貝が新鮮で美味しく、タレが濃厚です。バター炒めの香螺や塩唐辛子焼きのカニ爪を試して、サイゴン特有のシーフードの味を味わってください。"
            },

            new Place
            {
                Name = "Ốc Phát",
                Address = "361 Vĩnh Khánh, Q4",
                Specialty = "Ốc Phát - tươi sống, giá bình dân, hải sản đa dạng",
                ImageUrl = "oc-phat.jpg",
                Latitude = 10.7601,
                Longitude = 106.7019,
                ActivationRadiusMeters = 37,
                Priority = 3,
                Description = "Ốc Phát là một trong những quán ốc lâu năm và đông khách trên phố Vĩnh Khánh, nổi bật với hải sản tươi sống được chế biến ngay tại chỗ. Các món được yêu thích như ốc hương xào bơ tỏi, ốc len xào dừa, càng cua rang me hay sò điệp nướng mỡ hành – vị đậm đà, thơm ngon khó cưỡng.",
                VietnameseAudioText = "Bạn đang ở khu vực Ốc Phát nổi tiếng trên phố Vĩnh Khánh. Hải sản tươi sống, giá bình dân. Hãy thử ốc hương xào bơ tỏi, ốc len xào dừa hoặc càng cua rang me để thưởng thức vị hải sản đậm đà Sài Gòn.",
                EnglishAudioText = "You are now at Oc Phat, a popular spot on Vinh Khanh food street. Fresh live seafood at affordable prices. Try butter-garlic stir-fried snails, coconut stir-fried snails, or tamarind-grilled crab claws for that bold Saigon seafood flavor.",
                ChineseAudioText = "您正在永庆街著名的Oc Phát区域。海鲜新鲜活跳、价格亲民。请尝试黄油蒜炒香螺、椰子炒田螺或酸角烤蟹爪，以品尝西贡浓郁的海鲜风味。",
                JapaneseAudioText = "あなたは今、ヴィン・カイン通りで有名なOc Phátエリアにいます。新鮮な活海鮮で手頃な価格です。バターガーリック炒めの香螺、ココナッツ炒めの田螺、またはタマリンド焼きのカニ爪を試して、サイゴンの濃厚なシーフードの味をお楽しみください。"
            }
        };
    }

    [RelayCommand]
    private async Task GoToDetail(Place place)
    {
        var param = new Dictionary<string, object> { { "Place", place } };
        await Shell.Current.GoToAsync(nameof(ProjectDetailPage), param);
    }
}