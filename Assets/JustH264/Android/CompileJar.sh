export Filename=com/newchromantics/justh264/H264Decoder
export ANDROID_JAR=/usr/local/var/lib/android-sdk/platforms/android-23/android.jar 
export UNITY_JAR=/Applications/Unity2017.2.0f3/PlaybackEngines/AndroidPlayer/Variations/mono/Release/Classes/classes.jar

#export JAVAPATH=/Library/Java/JavaVirtualMachines/1.6.0.jdk/Contents/Home/bin
#export JAVAPATH=/Library/Java/JavaVirtualMachines/zulu1.7.0_161.jdk/Contents/Home/bin
#export JAVAPATH=$JAVA_HOME/bin
export JAVAPATH=`/usr/libexec/java_home`/bin

rm $Filename.class
rm $Filename.jar

$JAVAPATH/javac -classpath $ANDROID_JAR $Filename.java -Xlint:deprecation -source 1.7 -target 1.7
$JAVAPATH/jar cvf $Filename.jar $Filename.class
$JAVAPATH/javap -cp $Filename.jar com.newchromantics.justh264.H264Decoder 

rm $Filename.class
