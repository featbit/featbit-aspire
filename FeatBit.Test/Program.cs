using FeatBit.Sdk.Server;
using FeatBit.Sdk.Server.Model;
using FeatBit.Sdk.Server.Options;

// setup SDK options
var options = new FbOptionsBuilder("server-key-qki1UwaaPLmRBw")
    .Event(new Uri("https://app-eval.featbit.co"))
    .Streaming(new Uri("wss://app-eval.featbit.co"))
    .Build();


// Creates a new client instance that connects to FeatBit with the custom option.
var client = new FbClient(options);
if (!client.Initialized)
{
    Console.WriteLine("FbClient failed to initialize. All Variation calls will use fallback value.");
}
else
{
    Console.WriteLine("FbClient successfully initialized!");
}

// flag to be evaluated
const string flagKey = "game-runner";

// create a user
var user = FbUser.Builder("anonymous").Build();


for (int i = 0; i < 2000; i++)
{
    await Task.Delay(200);

    // evaluate a boolean flag for a given user
    var boolVariation = client.BoolVariation(flagKey, user, defaultValue: false);
    Console.WriteLine($"flag '{flagKey}' returns {boolVariation} for user {user.Key}");

    // evaluate a boolean flag for a given user with evaluation detail
    var boolVariationDetail = client.BoolVariationDetail(flagKey, user, defaultValue: false);
    Console.WriteLine(
        $"flag '{flagKey}' returns {boolVariationDetail.Value} for user {user.Key}. " +
        $"Reason Kind: {boolVariationDetail.Kind}, Reason Description: {boolVariationDetail.Reason}"
    );
}



// close the client to ensure that all insights are sent out before the app exits
await client.CloseAsync();